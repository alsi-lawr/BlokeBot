using BlokeBot.Core.Features.TwitchOperations.ClipsMarkers;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Moments;

public sealed class MomentProviderOperations(
    ClipMarkerService provider,
    IDbContextFactory<BlokeBotDbContext> dbFactory
) : IMomentProviderOperations
{
    public async Task<MomentProviderOutcome> CaptureAsync(
        int hostId,
        Guid publicId,
        bool markerFallbackEnabled,
        string description,
        CancellationToken ct
    )
    {
        var clipKey = $"moment:{publicId:N}:clip";
        var clipOutcome = await provider.CreateMomentClipAsync(hostId, false, clipKey, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var clip = await db.TwitchClips.SingleOrDefaultAsync(
            value => value.HostId == hostId && value.IdempotencyKey == clipKey,
            ct
        );
        switch (clipOutcome)
        {
            case ClipMarkerOperationOutcome.ClipAvailable:
                return new MomentProviderOutcome.ClipReady(clip!.Id);
            case ClipMarkerOperationOutcome.ClipPending:
                return new MomentProviderOutcome.Pending(clip!.Id);
            case ClipMarkerOperationOutcome.ClipAmbiguous:
                return new MomentProviderOutcome.Ambiguous(
                    clip?.Id,
                    null,
                    "Twitch did not confirm whether the clip request completed."
                );
            case ClipMarkerOperationOutcome.ClipFailed failed
                when clip?.Status == TwitchClipStatus.Expired:
                return await FallbackOrFailAsync(
                    db,
                    hostId,
                    publicId,
                    clip,
                    markerFallbackEnabled,
                    description,
                    failed.Clip.FailureReason ?? "Twitch clip creation expired.",
                    ct
                );
            case ClipMarkerOperationOutcome.ProviderRejected rejected:
                return await FallbackOrFailAsync(
                    db,
                    hostId,
                    publicId,
                    clip,
                    markerFallbackEnabled,
                    description,
                    rejected.Message,
                    ct
                );
            case ClipMarkerOperationOutcome.Offline:
                return Failed(clip, "Twitch reports that the channel is offline.");
            case ClipMarkerOperationOutcome.VodsDisabled:
                return Failed(clip, "Twitch reports that VOD or clip creation is disabled.");
            case ClipMarkerOperationOutcome.RerunOrPremiere:
                return Failed(
                    clip,
                    "Twitch reports that clips are unavailable for this rerun or premiere."
                );
            case ClipMarkerOperationOutcome.NotReady when clip?.Status == TwitchClipStatus.Pending:
                return new MomentProviderOutcome.Pending(clip.Id);
            case ClipMarkerOperationOutcome.NotReady
                when clip?.Status == TwitchClipStatus.Ambiguous:
                return new MomentProviderOutcome.Ambiguous(
                    clip.Id,
                    null,
                    clip.FailureReason
                        ?? "Twitch did not confirm whether the clip request completed."
                );
            case ClipMarkerOperationOutcome.NotReady notReady:
                return Failed(clip, notReady.Message);
            case ClipMarkerOperationOutcome.InvalidRequest invalid:
                return Failed(clip, invalid.Message);
            case ClipMarkerOperationOutcome.ClipFailed failed:
                return Failed(clip, failed.Clip.FailureReason ?? "Twitch clip creation failed.");
            default:
                return Failed(clip, "Twitch clip creation did not complete.");
        }
    }

    private async Task<MomentProviderOutcome> FallbackOrFailAsync(
        BlokeBotDbContext db,
        int hostId,
        Guid publicId,
        TwitchClip? clip,
        bool markerFallbackEnabled,
        string description,
        string clipFailure,
        CancellationToken ct
    )
    {
        if (!markerFallbackEnabled)
        {
            return Failed(clip, clipFailure);
        }
        var markerKey = $"moment:{publicId:N}:marker";
        var markerOutcome = await provider.CreateMomentMarkerAsync(
            hostId,
            description,
            markerKey,
            ct
        );
        var marker = await db.TwitchStreamMarkers.SingleOrDefaultAsync(
            value => value.HostId == hostId && value.IdempotencyKey == markerKey,
            ct
        );
        return markerOutcome switch
        {
            ClipMarkerOperationOutcome.MarkerCreated => new MomentProviderOutcome.MarkerReady(
                marker!.Id
            ),
            ClipMarkerOperationOutcome.MarkerAmbiguous => new MomentProviderOutcome.Ambiguous(
                clip?.Id,
                marker?.Id,
                "Twitch did not confirm whether the fallback marker completed."
            ),
            ClipMarkerOperationOutcome.MarkerFailed failed => new MomentProviderOutcome.Failed(
                clip?.Id,
                marker?.Id,
                failed.Marker.FailureReason ?? clipFailure
            ),
            ClipMarkerOperationOutcome.NotReady notReady => new MomentProviderOutcome.Failed(
                clip?.Id,
                marker?.Id,
                notReady.Message
            ),
            ClipMarkerOperationOutcome.Offline => new MomentProviderOutcome.Failed(
                clip?.Id,
                marker?.Id,
                "Twitch reports that the channel is offline."
            ),
            ClipMarkerOperationOutcome.VodsDisabled => new MomentProviderOutcome.Failed(
                clip?.Id,
                marker?.Id,
                "Twitch reports that markers are unavailable because VODs are disabled."
            ),
            ClipMarkerOperationOutcome.RerunOrPremiere => new MomentProviderOutcome.Failed(
                clip?.Id,
                marker?.Id,
                "Twitch reports that markers are unavailable for this rerun or premiere."
            ),
            ClipMarkerOperationOutcome.ProviderRejected rejected =>
                new MomentProviderOutcome.Failed(clip?.Id, marker?.Id, rejected.Message),
            ClipMarkerOperationOutcome.InvalidRequest invalid => new MomentProviderOutcome.Failed(
                clip?.Id,
                marker?.Id,
                invalid.Message
            ),
            _ => new MomentProviderOutcome.Failed(clip?.Id, marker?.Id, clipFailure),
        };
    }

    private static MomentProviderOutcome Failed(TwitchClip? clip, string reason)
    {
        return new MomentProviderOutcome.Failed(clip?.Id, null, reason);
    }
}
