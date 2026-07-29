using BlokeBot.Core.Features.Alerts;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Eventing;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using BlokeBot.Twitch;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.TwitchOperations.ClipsMarkers;

public sealed class ClipMarkerService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    IHostBroadcasterTokenStatusProvider broadcasters,
    HelixClient helix,
    BotSettings settings,
    EventBus<AppEventKind> events,
    DurableAlertService alerts,
    TimeProvider timeProvider,
    NativeTwitchFeatureGate nativeTwitch
) : IClipMarkerDashboardOperations
{
    private const int _resultsToKeep = 100;
    private static readonly TimeSpan _clipAvailabilityBound = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan _clipPollCadence = TimeSpan.FromSeconds(1);

    public async Task<ClipMarkerDashboardState> LoadAsync(int hostId, CancellationToken ct)
    {
        if (!await nativeTwitch.IsEnabledAsync(hostId, ct))
        {
            return new(new ClipMarkerAuthorizationReadiness.Disabled(), [], [], []);
        }

        await ReconcileAsync(hostId, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var readiness = await ReadinessAsync(hostId, ct);
        var pending = await db
            .TwitchClips.AsNoTracking()
            .Where(clip => clip.HostId == hostId && clip.Status == TwitchClipStatus.Pending)
            .OrderByDescending(clip => clip.RequestedAtUtc)
            .ToArrayAsync(ct);
        var results = await db
            .TwitchClips.AsNoTracking()
            .Where(clip => clip.HostId == hostId && clip.Status != TwitchClipStatus.Pending)
            .OrderByDescending(clip => clip.ResolvedAtUtc)
            .Take(_resultsToKeep)
            .ToArrayAsync(ct);
        var markers = await db
            .TwitchStreamMarkers.AsNoTracking()
            .Where(marker => marker.HostId == hostId)
            .OrderByDescending(marker => marker.CreatedAtUtc)
            .Take(_resultsToKeep)
            .ToArrayAsync(ct);
        return new(
            readiness,
            pending.Select(View).ToArray(),
            results.Select(View).ToArray(),
            markers.Select(View).ToArray()
        );
    }

    public async Task<ClipMarkerOperationOutcome> CreateClipAsync(
        int hostId,
        bool hasDelay,
        CancellationToken ct
    )
    {
        if (!await nativeTwitch.IsEnabledAsync(hostId, ct))
        {
            return new ClipMarkerOperationOutcome.NotReady(NativeTwitchFeatureGate.DisabledMessage);
        }

        var token = await ReadyTokenAsync(hostId, ct);
        if (token is null)
        {
            return new ClipMarkerOperationOutcome.NotReady(
                "Reconnect the selected broadcaster with Twitch operations permissions."
            );
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var host = await db.Hosts.SingleOrDefaultAsync(host => host.Id == hostId, ct);
        if (host?.TwitchUserId is not { Length: > 0 } broadcasterId)
        {
            return new ClipMarkerOperationOutcome.ProviderRejected(
                "The selected channel is unavailable."
            );
        }
        if (!host.EnabledFeatures.Contains(HostFeatureFlags.NativeTwitch))
        {
            return new ClipMarkerOperationOutcome.NotReady(NativeTwitchFeatureGate.DisabledMessage);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var clip = new TwitchClip
        {
            HostId = hostId,
            IdempotencyKey = NewAttemptKey(),
            Status = TwitchClipStatus.Pending,
            RequestedAtUtc = now,
        };
        db.TwitchClips.Add(clip);
        await db.SaveChangesAsync(ct);

        if (!await nativeTwitch.IsEnabledAsync(hostId, ct))
        {
            return new ClipMarkerOperationOutcome.NotReady(NativeTwitchFeatureGate.DisabledMessage);
        }

        var provider = await helix.CreateClipAsync(
            new HelixRequestContext(settings.Identity.ClientId, token),
            broadcasterId,
            hasDelay,
            ct
        );
        switch (provider)
        {
            case HelixClipCreateOutcome.Created created:
                clip.ProviderClipId = created.Clip.Id;
                clip.EditUrl = created.Clip.EditUrl;
                await db.SaveChangesAsync(ct);
                await events.PublishAsync(AppEventKind.TwitchOperationsChanged, ct);
                await ReconcileAsync(hostId, ct);
                await db.Entry(clip).ReloadAsync(ct);
                return Outcome(clip);
            case HelixClipCreateOutcome.Offline:
                return await CompleteClipAsync(
                    db,
                    clip,
                    TwitchClipStatus.Failed,
                    "Twitch reports that the channel is offline.",
                    ct,
                    new ClipMarkerOperationOutcome.Offline()
                );
            case HelixClipCreateOutcome.VodsDisabled:
                return await CompleteClipAsync(
                    db,
                    clip,
                    TwitchClipStatus.Failed,
                    "Twitch reports that VOD or clip creation is disabled.",
                    ct,
                    new ClipMarkerOperationOutcome.VodsDisabled()
                );
            case HelixClipCreateOutcome.RerunOrPremiere:
                return await CompleteClipAsync(
                    db,
                    clip,
                    TwitchClipStatus.Failed,
                    "Twitch reports that clips are unavailable for this rerun or premiere.",
                    ct,
                    new ClipMarkerOperationOutcome.RerunOrPremiere()
                );
            case HelixClipCreateOutcome.Unauthorized:
                return await CompleteClipAsync(
                    db,
                    clip,
                    TwitchClipStatus.Failed,
                    "Twitch did not accept the selected broadcaster authorization.",
                    ct,
                    new ClipMarkerOperationOutcome.NotReady(
                        "Reconnect the selected broadcaster with Twitch operations permissions."
                    )
                );
            case HelixClipCreateOutcome.Ambiguous:
                return await CompleteClipAsync(
                    db,
                    clip,
                    TwitchClipStatus.Ambiguous,
                    "Twitch did not confirm whether the clip request completed.",
                    ct,
                    new ClipMarkerOperationOutcome.ClipAmbiguous(new ClipAttemptReference(clip.Id))
                );
            case HelixClipCreateOutcome.ProviderRejected:
                return await CompleteClipAsync(
                    db,
                    clip,
                    TwitchClipStatus.Failed,
                    "Twitch did not permit creating a clip.",
                    ct,
                    new ClipMarkerOperationOutcome.ProviderRejected(
                        "Twitch did not permit creating a clip."
                    )
                );
            default:
                throw new InvalidOperationException("Unknown Twitch clip creation outcome.");
        }
    }

    public async Task<ClipMarkerOperationOutcome> CreateMarkerAsync(
        int hostId,
        string description,
        CancellationToken ct
    )
    {
        if (!await nativeTwitch.IsEnabledAsync(hostId, ct))
        {
            return new ClipMarkerOperationOutcome.NotReady(NativeTwitchFeatureGate.DisabledMessage);
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            return new ClipMarkerOperationOutcome.InvalidRequest(
                "A marker description is required."
            );
        }

        var token = await ReadyTokenAsync(hostId, ct);
        if (token is null)
        {
            return new ClipMarkerOperationOutcome.NotReady(
                "Reconnect the selected broadcaster with Twitch operations permissions."
            );
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var host = await db.Hosts.SingleOrDefaultAsync(host => host.Id == hostId, ct);
        if (host?.TwitchUserId is not { Length: > 0 } broadcasterId)
        {
            return new ClipMarkerOperationOutcome.ProviderRejected(
                "The selected channel is unavailable."
            );
        }
        if (!host.EnabledFeatures.Contains(HostFeatureFlags.NativeTwitch))
        {
            return new ClipMarkerOperationOutcome.NotReady(NativeTwitchFeatureGate.DisabledMessage);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var marker = new TwitchStreamMarker
        {
            HostId = hostId,
            IdempotencyKey = NewAttemptKey(),
            Description = description.Trim(),
            Status = TwitchStreamMarkerStatus.Ambiguous,
            CreatedAtUtc = now,
        };
        db.TwitchStreamMarkers.Add(marker);
        await db.SaveChangesAsync(ct);

        if (!await nativeTwitch.IsEnabledAsync(hostId, ct))
        {
            return new ClipMarkerOperationOutcome.NotReady(NativeTwitchFeatureGate.DisabledMessage);
        }

        var provider = await helix.CreateStreamMarkerAsync(
            new HelixRequestContext(settings.Identity.ClientId, token),
            broadcasterId,
            marker.Description,
            ct
        );
        switch (provider)
        {
            case HelixStreamMarkerCreateOutcome.Created created:
                marker.Status = TwitchStreamMarkerStatus.Succeeded;
                marker.ProviderMarkerId = created.Marker.Id;
                marker.Description = created.Marker.Description;
                marker.PositionSeconds = created.Marker.PositionSeconds;
                marker.MarkerUrl = created.Marker.Url;
                marker.VideoId = created.Marker.VideoId;
                marker.CreatedAtUtc = created.Marker.CreatedAt.UtcDateTime;
                marker.ResolvedAtUtc = now;
                await db.SaveChangesAsync(ct);
                await TrimMarkersAsync(db, hostId, ct);
                await db.SaveChangesAsync(ct);
                await events.PublishAsync(AppEventKind.TwitchOperationsChanged, ct);
                return new ClipMarkerOperationOutcome.MarkerCreated(View(marker));
            case HelixStreamMarkerCreateOutcome.Offline:
                return await CompleteMarkerAsync(
                    db,
                    marker,
                    TwitchStreamMarkerStatus.Failed,
                    "Twitch reports that the channel is offline.",
                    ct,
                    new ClipMarkerOperationOutcome.Offline()
                );
            case HelixStreamMarkerCreateOutcome.VodsDisabled:
                return await CompleteMarkerAsync(
                    db,
                    marker,
                    TwitchStreamMarkerStatus.Failed,
                    "Twitch reports that VODs are disabled for stream markers.",
                    ct,
                    new ClipMarkerOperationOutcome.VodsDisabled()
                );
            case HelixStreamMarkerCreateOutcome.RerunOrPremiere:
                return await CompleteMarkerAsync(
                    db,
                    marker,
                    TwitchStreamMarkerStatus.Failed,
                    "Twitch reports that markers are unavailable for this rerun or premiere.",
                    ct,
                    new ClipMarkerOperationOutcome.RerunOrPremiere()
                );
            case HelixStreamMarkerCreateOutcome.Unauthorized:
                return await CompleteMarkerAsync(
                    db,
                    marker,
                    TwitchStreamMarkerStatus.Failed,
                    "Twitch did not accept the selected broadcaster authorization.",
                    ct,
                    new ClipMarkerOperationOutcome.NotReady(
                        "Reconnect the selected broadcaster with Twitch operations permissions."
                    )
                );
            case HelixStreamMarkerCreateOutcome.Ambiguous:
                return await CompleteMarkerAsync(
                    db,
                    marker,
                    TwitchStreamMarkerStatus.Ambiguous,
                    "Twitch did not confirm whether the marker request completed.",
                    ct,
                    new ClipMarkerOperationOutcome.MarkerAmbiguous(
                        new StreamMarkerAttemptReference(marker.Id)
                    )
                );
            case HelixStreamMarkerCreateOutcome.ProviderRejected:
                return await CompleteMarkerAsync(
                    db,
                    marker,
                    TwitchStreamMarkerStatus.Failed,
                    "Twitch did not permit creating a stream marker.",
                    ct,
                    new ClipMarkerOperationOutcome.ProviderRejected(
                        "Twitch did not permit creating a stream marker."
                    )
                );
            default:
                throw new InvalidOperationException(
                    "Unknown Twitch stream-marker creation outcome."
                );
        }
    }

    public async Task<ClipMarkerOperationOutcome> RetryClipAsync(
        int hostId,
        ClipAttemptReference attempt,
        CancellationToken ct
    )
    {
        if (!await nativeTwitch.IsEnabledAsync(hostId, ct))
        {
            return new ClipMarkerOperationOutcome.NotReady(NativeTwitchFeatureGate.DisabledMessage);
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var clip = await db.TwitchClips.SingleOrDefaultAsync(
            candidate => candidate.HostId == hostId && candidate.Id == attempt.Value,
            ct
        );
        if (clip is null)
        {
            return new ClipMarkerOperationOutcome.InvalidRequest(
                "The selected clip attempt is no longer available."
            );
        }

        if (clip.Status == TwitchClipStatus.Pending)
        {
            await ReconcileAsync(hostId, ct);
            await db.Entry(clip).ReloadAsync(ct);
        }

        return Outcome(clip);
    }

    public async Task<ClipMarkerOperationOutcome> RetryMarkerAsync(
        int hostId,
        StreamMarkerAttemptReference attempt,
        CancellationToken ct
    )
    {
        if (!await nativeTwitch.IsEnabledAsync(hostId, ct))
        {
            return new ClipMarkerOperationOutcome.NotReady(NativeTwitchFeatureGate.DisabledMessage);
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var marker = await db.TwitchStreamMarkers.SingleOrDefaultAsync(
            candidate => candidate.HostId == hostId && candidate.Id == attempt.Value,
            ct
        );
        return marker is null
            ? new ClipMarkerOperationOutcome.InvalidRequest(
                "The selected marker attempt is no longer available."
            )
            : MarkerOutcome(marker);
    }

    public async Task ReconcileChannelAsync(string channel, CancellationToken ct)
    {
        var login = Login.Normalize(channel);
        if (login.Length == 0)
        {
            return;
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var hostId = await db
            .Hosts.Where(host => host.Login == login)
            .Select(host => (int?)host.Id)
            .SingleOrDefaultAsync(ct);
        if (hostId is { } id)
        {
            await ReconcileAsync(id, ct);
        }
    }

    public async Task ReconcileAsync(int hostId, CancellationToken ct)
    {
        if (!await nativeTwitch.IsEnabledAsync(hostId, ct))
        {
            return;
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var host = await db.Hosts.SingleOrDefaultAsync(host => host.Id == hostId, ct);
        if (
            host?.TwitchUserId is not { Length: > 0 } broadcasterId
            || !host.EnabledFeatures.Contains(HostFeatureFlags.NativeTwitch)
        )
        {
            return;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var pending = await db
            .TwitchClips.Where(clip =>
                clip.HostId == hostId && clip.Status == TwitchClipStatus.Pending
            )
            .ToArrayAsync(ct);
        if (ExpireClips(pending, now) && await nativeTwitch.IsEnabledAsync(hostId, ct))
        {
            await SaveReconciliationChangesAsync(db, hostId, ct);
        }

        var deadline = pending
            .Where(clip => clip.Status == TwitchClipStatus.Pending)
            .Select(clip => clip.RequestedAtUtc + _clipAvailabilityBound)
            .DefaultIfEmpty()
            .Max();
        var deadlineRemaining =
            deadline == default ? (TimeSpan?)null : deadline - timeProvider.GetUtcNow().UtcDateTime;
        if (deadlineRemaining is { } remaining && remaining <= TimeSpan.Zero)
        {
            await ExpirePendingClipsAsync(db, hostId, ct);
            return;
        }

        using var deadlineCancellation = deadlineRemaining is { } remainingDuration
            ? new CancellationTokenSource(remainingDuration, timeProvider)
            : null;
        using var reconciliationCancellation = deadlineCancellation is null
            ? null
            : CancellationTokenSource.CreateLinkedTokenSource(ct, deadlineCancellation.Token);
        var reconciliationToken = reconciliationCancellation?.Token ?? ct;

        try
        {
            var token = await ReadyTokenAsync(hostId, reconciliationToken);
            if (!await nativeTwitch.IsEnabledAsync(hostId, reconciliationToken))
            {
                return;
            }

            await PollPendingClipsAsync(db, hostId, token, reconciliationToken, ct);

            if (token is null || !await nativeTwitch.IsEnabledAsync(hostId, reconciliationToken))
            {
                return;
            }
            var markers = await db
                .TwitchStreamMarkers.Where(marker =>
                    marker.HostId == hostId && marker.Status == TwitchStreamMarkerStatus.Succeeded
                )
                .ToArrayAsync(reconciliationToken);
            if (markers.Length > 0)
            {
                if (!await nativeTwitch.IsEnabledAsync(hostId, reconciliationToken))
                {
                    return;
                }

                var providerMarkers = await helix.GetStreamMarkersAsync(
                    new HelixRequestContext(settings.Identity.ClientId, token),
                    broadcasterId,
                    markers
                        .Select(marker => marker.ProviderMarkerId)
                        .OfType<string>()
                        .ToHashSet(StringComparer.Ordinal),
                    reconciliationToken
                );
                if (!await nativeTwitch.IsEnabledAsync(hostId, reconciliationToken))
                {
                    return;
                }

                var changed = false;
                if (providerMarkers is HelixStreamMarkerLookupOutcome.Found found)
                {
                    foreach (var marker in markers)
                    {
                        var provider = found.Markers.FirstOrDefault(item =>
                            item.Id == marker.ProviderMarkerId
                        );
                        if (
                            provider is null
                            || (
                                marker.VideoId == provider.VideoId
                                && marker.MarkerUrl == provider.Url
                            )
                        )
                        {
                            continue;
                        }

                        marker.VideoId = provider.VideoId;
                        marker.MarkerUrl = provider.Url;
                        marker.EnrichedAtUtc = now;
                        changed = true;
                    }
                }
                if (changed)
                {
                    await SaveReconciliationChangesAsync(db, hostId, ct);
                }
            }
        }
        catch (OperationCanceledException)
            when (!ct.IsCancellationRequested
                && deadlineCancellation?.IsCancellationRequested == true
            )
        {
            await ExpirePendingClipsAsync(db, hostId, ct);
        }
    }

    private async Task PollPendingClipsAsync(
        BlokeBotDbContext db,
        int hostId,
        string? token,
        CancellationToken pollingToken,
        CancellationToken ct
    )
    {
        var pending = await db
            .TwitchClips.Where(clip =>
                clip.HostId == hostId && clip.Status == TwitchClipStatus.Pending
            )
            .ToArrayAsync(pollingToken);
        if (pending.Length == 0)
        {
            return;
        }

        while (pending.Any(clip => clip.Status == TwitchClipStatus.Pending))
        {
            var now = timeProvider.GetUtcNow().UtcDateTime;
            var changed = ExpireClips(pending, now);
            foreach (var clip in pending.Where(clip => clip.Status == TwitchClipStatus.Pending))
            {
                if (token is null || clip.ProviderClipId is null)
                {
                    continue;
                }
                if (!await nativeTwitch.IsEnabledAsync(hostId, pollingToken))
                {
                    return;
                }

                var provider = await helix.GetClipAsync(
                    new HelixRequestContext(settings.Identity.ClientId, token),
                    clip.ProviderClipId!,
                    pollingToken
                );
                if (!await nativeTwitch.IsEnabledAsync(hostId, pollingToken))
                {
                    return;
                }

                clip.LastCheckedAtUtc = now;
                if (provider is HelixClipLookupOutcome.Found found)
                {
                    Apply(clip, found.Clip);
                    clip.Status = TwitchClipStatus.Available;
                    clip.ResolvedAtUtc = now;
                    changed = true;
                }
            }

            if (changed && await nativeTwitch.IsEnabledAsync(hostId, ct))
            {
                await SaveReconciliationChangesAsync(db, hostId, ct);
            }

            if (!pending.Any(clip => clip.Status == TwitchClipStatus.Pending))
            {
                return;
            }

            await Task.Delay(_clipPollCadence, timeProvider, pollingToken);
        }
    }

    private async Task ExpirePendingClipsAsync(
        BlokeBotDbContext db,
        int hostId,
        CancellationToken ct
    )
    {
        var pending = await db
            .TwitchClips.Where(clip =>
                clip.HostId == hostId && clip.Status == TwitchClipStatus.Pending
            )
            .ToArrayAsync(ct);
        if (ExpireClips(pending, timeProvider.GetUtcNow().UtcDateTime))
        {
            await SaveReconciliationChangesAsync(db, hostId, ct);
        }
    }

    private static bool ExpireClips(IEnumerable<TwitchClip> clips, DateTime now)
    {
        var changed = false;
        foreach (var clip in clips)
        {
            if (
                clip.Status != TwitchClipStatus.Pending
                || now - clip.RequestedAtUtc < _clipAvailabilityBound
            )
            {
                continue;
            }

            clip.Status = TwitchClipStatus.Expired;
            clip.FailureReason = "Twitch did not make the clip available within 60 seconds.";
            clip.ResolvedAtUtc = now;
            changed = true;
        }

        return changed;
    }

    private async Task SaveReconciliationChangesAsync(
        BlokeBotDbContext db,
        int hostId,
        CancellationToken ct
    )
    {
        await db.SaveChangesAsync(ct);
        await TrimClipsAsync(db, hostId, ct);
        await TrimMarkersAsync(db, hostId, ct);
        await db.SaveChangesAsync(ct);
        await events.PublishAsync(AppEventKind.TwitchOperationsChanged, ct);
    }

    private async Task<ClipMarkerAuthorizationReadiness> ReadinessAsync(
        int hostId,
        CancellationToken ct
    )
    {
        var status = await broadcasters.GetTokenStatusAsync(
            hostId,
            HostBroadcasterAuthorizationService.MilestoneScopes,
            ct
        );
        if (status is TokenStatus.Ready)
        {
            return new ClipMarkerAuthorizationReadiness.Ready();
        }

        await EnsureBroadcasterAuthorizationAlertAsync(hostId, ct);
        return new ClipMarkerAuthorizationReadiness.NeedsBroadcasterAuthorization(
            "Reconnect the selected broadcaster with Twitch operations permissions."
        );
    }

    private async Task<string?> ReadyTokenAsync(int hostId, CancellationToken ct)
    {
        var status = await broadcasters.GetTokenStatusAsync(
            hostId,
            HostBroadcasterAuthorizationService.MilestoneScopes,
            ct
        );
        if (status is TokenStatus.Ready ready)
        {
            return ready.AccessToken;
        }

        await EnsureBroadcasterAuthorizationAlertAsync(hostId, ct);
        return null;
    }

    private async Task EnsureBroadcasterAuthorizationAlertAsync(int hostId, CancellationToken ct)
    {
        await alerts
            .Create(
                hostId,
                DurableAlertSeverity.Warning,
                "twitch-broadcaster-authorization",
                "reauthorize-v1",
                "Reconnect broadcaster for Twitch operations",
                "Twitch operations needs the selected broadcaster to reconnect and approve all requested permissions.",
                "/twitch-operations"
            )
            .ExecuteAsync(ct);
    }

    private async Task<ClipMarkerOperationOutcome> CompleteClipAsync(
        BlokeBotDbContext db,
        TwitchClip clip,
        TwitchClipStatus status,
        string reason,
        CancellationToken ct,
        ClipMarkerOperationOutcome outcome
    )
    {
        clip.Status = status;
        clip.FailureReason = reason;
        clip.ResolvedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        await TrimClipsAsync(db, clip.HostId, ct);
        await db.SaveChangesAsync(ct);
        await events.PublishAsync(AppEventKind.TwitchOperationsChanged, ct);
        return outcome;
    }

    private async Task<ClipMarkerOperationOutcome> CompleteMarkerAsync(
        BlokeBotDbContext db,
        TwitchStreamMarker marker,
        TwitchStreamMarkerStatus status,
        string reason,
        CancellationToken ct,
        ClipMarkerOperationOutcome outcome
    )
    {
        marker.Status = status;
        marker.FailureReason = reason;
        marker.ResolvedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        await TrimMarkersAsync(db, marker.HostId, ct);
        await db.SaveChangesAsync(ct);
        await events.PublishAsync(AppEventKind.TwitchOperationsChanged, ct);
        return outcome;
    }

    private static void Apply(TwitchClip record, HelixClip clip)
    {
        record.ProviderClipId = clip.Id;
        record.FinalUrl = clip.Url;
        record.EditUrl ??= clip.EditUrl;
        record.BroadcasterTwitchUserId = clip.BroadcasterId;
        record.BroadcasterLogin = clip.BroadcasterLogin;
        record.CreatorTwitchUserId = clip.CreatorId;
        record.CreatorLogin = clip.CreatorLogin;
        record.VideoId = clip.VideoId;
    }

    private static async Task TrimClipsAsync(BlokeBotDbContext db, int hostId, CancellationToken ct)
    {
        var excess = await db
            .TwitchClips.Where(clip =>
                clip.HostId == hostId && clip.Status != TwitchClipStatus.Pending
            )
            .OrderByDescending(clip => clip.ResolvedAtUtc)
            .Skip(_resultsToKeep)
            .ToArrayAsync(ct);
        db.TwitchClips.RemoveRange(excess);
    }

    private static async Task TrimMarkersAsync(
        BlokeBotDbContext db,
        int hostId,
        CancellationToken ct
    )
    {
        var excess = await db
            .TwitchStreamMarkers.Where(marker => marker.HostId == hostId)
            .OrderByDescending(marker => marker.ResolvedAtUtc)
            .Skip(_resultsToKeep)
            .ToArrayAsync(ct);
        db.TwitchStreamMarkers.RemoveRange(excess);
    }

    private static ClipView View(TwitchClip clip)
    {
        return new(
            new ClipAttemptReference(clip.Id),
            clip.Status.ToString(),
            clip.ProviderClipId,
            clip.EditUrl,
            clip.FinalUrl,
            clip.CreatorLogin,
            clip.VideoId,
            clip.FailureReason,
            clip.RequestedAtUtc,
            clip.ResolvedAtUtc
        );
    }

    private static StreamMarkerView View(TwitchStreamMarker marker)
    {
        return new(
            new StreamMarkerAttemptReference(marker.Id),
            marker.Status.ToString(),
            marker.ProviderMarkerId,
            marker.Description,
            marker.PositionSeconds,
            marker.MarkerUrl,
            marker.VideoId,
            marker.FailureReason,
            marker.CreatedAtUtc
        );
    }

    private static ClipMarkerOperationOutcome Outcome(TwitchClip clip)
    {
        return clip.Status switch
        {
            TwitchClipStatus.Pending => new ClipMarkerOperationOutcome.ClipPending(View(clip)),
            TwitchClipStatus.Available => new ClipMarkerOperationOutcome.ClipAvailable(View(clip)),
            TwitchClipStatus.Ambiguous => new ClipMarkerOperationOutcome.ClipAmbiguous(
                new ClipAttemptReference(clip.Id)
            ),
            _ => new ClipMarkerOperationOutcome.ClipFailed(View(clip)),
        };
    }

    private static ClipMarkerOperationOutcome MarkerOutcome(TwitchStreamMarker marker)
    {
        return marker.Status switch
        {
            TwitchStreamMarkerStatus.Succeeded => new ClipMarkerOperationOutcome.MarkerCreated(
                View(marker)
            ),
            TwitchStreamMarkerStatus.Ambiguous => new ClipMarkerOperationOutcome.MarkerAmbiguous(
                new StreamMarkerAttemptReference(marker.Id)
            ),
            _ => new ClipMarkerOperationOutcome.MarkerFailed(View(marker)),
        };
    }

    private static string NewAttemptKey()
    {
        return Guid.NewGuid().ToString("N");
    }
}
