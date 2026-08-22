using System.Collections.Immutable;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Overlays;

internal sealed partial class OverlayCuePlaybackService
{
    private async Task MonitorAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250), timeProvider);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await ValidateAllAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private async Task ValidateAllAsync(CancellationToken cancellationToken)
    {
        foreach (var pair in _targets)
        {
            OverlayCuePlaybackPlan[] plans;
            lock (pair.Value.Gate)
            {
                plans = pair
                    .Value.Active.Values.Select(value => value.Plan)
                    .Concat(pair.Value.Pending.Select(value => value.Plan))
                    .ToArray();
            }
            bool valid;
            try
            {
                valid = await StateStillEnabledAsync(pair.Key, plans, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Cue target validation failed for host {HostId} and overlay {OverlayId}.",
                    pair.Key.HostId,
                    pair.Key.OverlayId
                );
                continue;
            }
            lock (pair.Value.Gate)
            {
                if (!valid)
                {
                    CancelAll(pair.Key, pair.Value);
                    continue;
                }
                ExpireAndAdvance(pair.Key, pair.Value);
            }
        }
    }

    private async Task<bool> StateStillEnabledAsync(
        OverlayTargetIdentity identity,
        IReadOnlyCollection<OverlayCuePlaybackPlan> plans,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var targetEnabled = await db
            .OverlayInstances.AsNoTracking()
            .Where(value =>
                value.HostId == identity.HostId
                && value.PublicId == identity.OverlayId
                && value.Type == OverlayType.CuePlayer
                && value.IsEnabled
            )
            .Join(
                db.Hosts.AsNoTracking(),
                overlay => overlay.HostId,
                host => host.Id,
                (_, host) => host.EnabledFeatures
            )
            .AnyAsync(
                features => (features & HostFeatureFlags.Overlays) == HostFeatureFlags.Overlays,
                cancellationToken
            );
        if (!targetEnabled)
        {
            return false;
        }
        if (plans.Count == 0)
        {
            return true;
        }
        var cueIds = plans.Select(plan => plan.CueId).Distinct().ToArray();
        var cues = await db
            .OverlayCues.AsNoTracking()
            .Where(cue =>
                cue.HostId == identity.HostId && cue.IsEnabled && cueIds.Contains(cue.PublicId)
            )
            .Select(cue => new { cue.PublicId, cue.Revision })
            .ToArrayAsync(cancellationToken);
        if (
            plans.Any(plan =>
                !cues.Any(cue => cue.PublicId == plan.CueId && cue.Revision == plan.CueRevision)
            )
        )
        {
            return false;
        }
        var assetVersions = plans
            .SelectMany(plan => plan.Layers)
            .OfType<OverlayCuePlaybackLayer.UploadedMedia>()
            .Select(layer => (layer.AssetId, layer.ContentRevision))
            .Distinct()
            .ToArray();
        if (assetVersions.Length == 0)
        {
            return true;
        }
        var assetIds = assetVersions.Select(value => value.AssetId).Distinct().ToArray();
        var assets = await db
            .OverlayMediaAssets.AsNoTracking()
            .Where(asset =>
                asset.HostId == identity.HostId
                && assetIds.Contains(asset.PublicId)
                && asset.Document.State == OverlayMediaDocumentState.Available
            )
            .Select(asset => new { asset.PublicId, asset.ContentRevision })
            .ToArrayAsync(cancellationToken);
        return assetVersions.All(version =>
            assets.Any(asset =>
                asset.PublicId == version.AssetId
                && asset.ContentRevision == version.ContentRevision
            )
        );
    }

    private async Task<bool> ParentEnabledAsync(int hostId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db
            .Hosts.AsNoTracking()
            .AnyAsync(
                host =>
                    host.Id == hostId
                    && (host.EnabledFeatures & HostFeatureFlags.Overlays)
                        == HostFeatureFlags.Overlays,
                cancellationToken
            );
    }

    private void ExpireAndAdvance(OverlayTargetIdentity identity, TargetState state)
    {
        var now = timeProvider.GetUtcNow();
        if (presence.Read(identity.HostId, identity.OverlayId).ActiveConnectionCount == 0)
        {
            var expiry = now.AddSeconds(
                options.Value.Overlays.Media.DisconnectedQueueExpirySeconds
            );
            var pendingCount = state.Pending.Count;
            for (var index = 0; index < pendingCount; index++)
            {
                var pending = state.Pending.Dequeue();
                state.Pending.Enqueue(
                    pending.ExpiresAtUtc == DateTimeOffset.MaxValue
                        ? pending with
                        {
                            ExpiresAtUtc = expiry,
                        }
                        : pending
                );
            }
        }
        foreach (
            var expired in state
                .Active.Values.Where(value =>
                    value.StartedAtUtc is { } started
                    && started.AddMilliseconds(value.Plan.DurationMilliseconds + 1000) <= now
                )
                .ToArray()
        )
        {
            _ = state.Active.Remove(expired.Plan.RunId);
            _ = state.Expired.Add(expired.Plan.RunId);
            transport.Stop(expired.Target, expired.Plan.RunId);
        }
        while (state.Pending.TryPeek(out var pending) && pending.ExpiresAtUtc <= now)
        {
            _ = state.Pending.Dequeue();
            _ = state.Expired.Add(pending.Plan.RunId);
        }
        Advance(identity, state);
        PruneTerminal(state);
    }

    private void Advance(OverlayTargetIdentity identity, TargetState state)
    {
        if (presence.Read(identity.HostId, identity.OverlayId).ActiveConnectionCount == 0)
        {
            return;
        }
        while (state.Pending.TryPeek(out var next))
        {
            if (state.Active.Count > 0 && next.QueuePolicy != OverlayCueQueuePolicy.Concurrent)
            {
                return;
            }
            _ = state.Pending.Dequeue();
            Start(identity, state, next);
            if (next.QueuePolicy != OverlayCueQueuePolicy.Concurrent)
            {
                return;
            }
        }
    }

    private void Start(OverlayTargetIdentity identity, TargetState state, AdmittedRun admitted)
    {
        var running = admitted with { StartedAtUtc = timeProvider.GetUtcNow() };
        state.Active.Add(running.Plan.RunId, running);
        transport.Start(running.Target, running.Plan);
    }

    private void CancelAll(OverlayTargetIdentity identity, TargetState state)
    {
        foreach (var active in state.Active.Values)
        {
            _ = state.Cancelled.Add(active.Plan.RunId);
            transport.Stop(active.Target, active.Plan.RunId);
        }
        foreach (var pending in state.Pending)
        {
            _ = state.Cancelled.Add(pending.Plan.RunId);
        }
        state.Active.Clear();
        state.Pending.Clear();
    }

    private static void PruneTerminal(TargetState state)
    {
        if (state.Expired.Count > 256)
        {
            state.Expired.Clear();
        }
        if (state.Cancelled.Count > 256)
        {
            state.Cancelled.Clear();
        }
    }
}
