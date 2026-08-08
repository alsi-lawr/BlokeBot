using System.Diagnostics;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.TwitchOperations.ClipsMarkers.Page;

public partial class ClipsMarkersPage
{
    private bool _clipHasDelay;
    private string _markerDescription = string.Empty;

    private bool _hasAttentionRequired =>
        State?.PendingClips.Count > 0
        || State?.Results.Any(static clip => clip.Status == "Ambiguous") == true
        || State?.Markers.Any(static marker => marker.Status == "Ambiguous") == true;

    protected override HostFeatureFlags Feature => HostFeatureFlags.ClipsAndMarkers;

    protected override async Task<ClipMarkerDashboardState?> LoadStateAsync(
        int hostId,
        CancellationToken cancellationToken
    ) => await _clipsMarkers.LoadAsync(hostId, cancellationToken);

    private Task CreateClipAsync() =>
        MutateAsync(hostId =>
            _clipsMarkers.CreateClipAsync(hostId, _clipHasDelay, CancellationToken.None)
        );

    private Task CreateMarkerAsync() =>
        MutateAsync(hostId =>
            _clipsMarkers.CreateMarkerAsync(hostId, _markerDescription, CancellationToken.None)
        );

    private Task RetryClipAsync(ClipAttemptReference attempt) =>
        MutateAsync(hostId =>
            _clipsMarkers.RetryClipAsync(hostId, attempt, CancellationToken.None)
        );

    private Task RetryMarkerAsync(StreamMarkerAttemptReference attempt) =>
        MutateAsync(hostId =>
            _clipsMarkers.RetryMarkerAsync(hostId, attempt, CancellationToken.None)
        );

    private Task MutateAsync(Func<int, Task<ClipMarkerOperationOutcome>> operation) =>
        MutateAsync(async hostId => Publish(await operation(hostId)));

    private void Publish(ClipMarkerOperationOutcome outcome)
    {
        var (message, success) = outcome switch
        {
            ClipMarkerOperationOutcome.ClipPending => (
                "Clip requested; Twitch is preparing it.",
                true
            ),
            ClipMarkerOperationOutcome.ClipAvailable => ("Clip is available.", true),
            ClipMarkerOperationOutcome.MarkerCreated => ("Stream marker created.", true),
            ClipMarkerOperationOutcome.NotReady => (
                "Reconnect this channel to Twitch, then try again.",
                false
            ),
            ClipMarkerOperationOutcome.InvalidRequest invalid => (invalid.Message, false),
            ClipMarkerOperationOutcome.Offline => (
                "Go live before creating a clip or marker.",
                false
            ),
            ClipMarkerOperationOutcome.VodsDisabled => (
                "Turn on stream recordings and clips in Twitch, then try again.",
                false
            ),
            ClipMarkerOperationOutcome.RerunOrPremiere => (
                "Clips and markers are not available during a rerun or premiere.",
                false
            ),
            ClipMarkerOperationOutcome.ClipAmbiguous => (
                "Twitch did not confirm whether the clip was made. Use Check outcome on this attempt.",
                false
            ),
            ClipMarkerOperationOutcome.MarkerAmbiguous => (
                "Twitch did not confirm whether the marker was made. Use Check outcome on this attempt.",
                false
            ),
            ClipMarkerOperationOutcome.ProviderRejected => (
                "Twitch could not complete that action. Check the stream and try again.",
                false
            ),
            ClipMarkerOperationOutcome.ClipFailed failed => (
                failed.Clip.FailureReason ?? "Twitch did not create the clip.",
                false
            ),
            ClipMarkerOperationOutcome.MarkerFailed failed => (
                failed.Marker.FailureReason ?? "Twitch did not create the marker.",
                false
            ),
            _ => throw new UnreachableException(),
        };
        Publish(message, success);
    }
}
