using System.Collections.Concurrent;
using System.Collections.Immutable;
using BlokeBot.Eventing;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BlokeBot.Core.Features.Overlays;

internal sealed class OverlayCuePlaybackService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    OverlayRemoteUrlPolicy urlPolicy,
    IOverlayLivePresence presence,
    IOverlayCueTransport transport,
    IOptions<BlokeBotOptions> options,
    EventBus<AppEventKind> events,
    TimeProvider timeProvider,
    ILogger<OverlayCuePlaybackService> logger
) : IOverlayCueAdmissionService, IHostedService, IAsyncDisposable
{
    private const int _maximumPendingPerTarget = 64;
    private readonly ConcurrentDictionary<OverlayTargetIdentity, TargetState> _targets = new();
    private readonly CancellationTokenSource _stopping = new();
    private Task? _monitor;
    private IDisposable? _changesSubscription;

    public async Task<OverlayCueReferenceOutcome> ResolveReferencesAsync(
        OverlayCueReferenceRequest request,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await ResolveReferencesAsync(db, request, cancellationToken) switch
        {
            ReferenceResolution.Available => new OverlayCueReferenceOutcome.Available(),
            ReferenceResolution.Missing missing => new OverlayCueReferenceOutcome.Missing(
                missing.Part
            ),
            ReferenceResolution.Disabled disabled => new OverlayCueReferenceOutcome.Disabled(
                disabled.Part
            ),
            _ => throw new InvalidOperationException("Unknown overlay cue reference outcome."),
        };
    }

    public async Task<OverlayCueAdmissionCatalog> QueryCatalogAsync(
        int hostId,
        CancellationToken cancellationToken
    )
    {
        if (hostId <= 0 || !await ParentEnabledAsync(hostId, cancellationToken))
        {
            return new([], []);
        }
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var targets = await db
            .OverlayInstances.AsNoTracking()
            .Where(value =>
                value.HostId == hostId && value.IsEnabled && value.Type == OverlayType.CuePlayer
            )
            .OrderBy(value => value.Name)
            .Select(value => new OverlayCueTargetChoice(value.PublicId, value.Name))
            .ToArrayAsync(cancellationToken);
        var cues = await db
            .OverlayCues.AsNoTracking()
            .Where(value => value.HostId == hostId && value.IsEnabled)
            .OrderBy(value => value.Name)
            .Select(value => new OverlayCueChoice(value.PublicId, value.Name, value.QueuePolicy))
            .ToArrayAsync(cancellationToken);
        return new(targets.ToImmutableArray(), cues.ToImmutableArray());
    }

    public async Task<OverlayCueAdmissionOutcome> AdmitAsync(
        OverlayCueAdmissionRequest request,
        CancellationToken cancellationToken
    )
    {
        if (
            request.HostId <= 0
            || request.TargetOverlayId == Guid.Empty
            || request.CueId == Guid.Empty
            || !Enum.IsDefined(request.QueuePolicy)
            || !Enum.IsDefined(request.Origin)
            || request.Context is null
            || request.Context.ViewerLogin.Length > 64
            || request.Context.ViewerDisplayName.Length > 128
        )
        {
            return new OverlayCueAdmissionOutcome.Missing();
        }

        var resolution = await ResolvePlanAsync(request, cancellationToken);
        if (resolution is not PlanResolution.Ready ready)
        {
            return resolution switch
            {
                PlanResolution.ParentDisabled =>
                    new OverlayCueAdmissionOutcome.ParentDisabledOrCancelled(),
                PlanResolution.Disabled => new OverlayCueAdmissionOutcome.Disabled(),
                _ => new OverlayCueAdmissionOutcome.Missing(),
            };
        }

        var identity = new OverlayTargetIdentity(request.HostId, request.TargetOverlayId);
        var state = _targets.GetOrAdd(identity, _ => new TargetState());
        lock (state.Gate)
        {
            PruneTerminal(state);
            var connected =
                presence.Read(request.HostId, request.TargetOverlayId).ActiveConnectionCount > 0;
            var busy = state.Active.Count > 0 || state.Pending.Count > 0;
            if (request.QueuePolicy == OverlayCueQueuePolicy.Ignore && busy)
            {
                return new OverlayCueAdmissionOutcome.QueueRejected();
            }
            if (request.QueuePolicy == OverlayCueQueuePolicy.Replace)
            {
                CancelAll(identity, state);
                busy = false;
            }
            if (state.Pending.Count + state.Active.Count >= _maximumPendingPerTarget)
            {
                return new OverlayCueAdmissionOutcome.QueueRejected();
            }

            var expiresAt = connected
                ? DateTimeOffset.MaxValue
                : timeProvider
                    .GetUtcNow()
                    .AddSeconds(options.Value.Overlays.Media.DisconnectedQueueExpirySeconds);
            var admitted = new AdmittedRun(
                ready.Target,
                ready.Plan,
                request.QueuePolicy,
                expiresAt
            );
            if (connected && (request.QueuePolicy == OverlayCueQueuePolicy.Concurrent || !busy))
            {
                Start(identity, state, admitted);
                return new OverlayCueAdmissionOutcome.Running(ready.Plan.RunId);
            }

            state.Pending.Enqueue(admitted);
            return connected
                ? new OverlayCueAdmissionOutcome.Queued(ready.Plan.RunId)
                : new OverlayCueAdmissionOutcome.Disconnected(ready.Plan.RunId, expiresAt);
        }
    }

    public Task<OverlayCueAdmissionOutcome> CompleteAsync(
        int hostId,
        Guid targetOverlayId,
        Guid runId,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var identity = new OverlayTargetIdentity(hostId, targetOverlayId);
        if (!_targets.TryGetValue(identity, out var state))
        {
            return Task.FromResult<OverlayCueAdmissionOutcome>(
                new OverlayCueAdmissionOutcome.Missing()
            );
        }
        lock (state.Gate)
        {
            if (!state.Active.Remove(runId, out var completed))
            {
                return Task.FromResult<OverlayCueAdmissionOutcome>(
                    state.Expired.Contains(runId)
                        ? new OverlayCueAdmissionOutcome.Expired()
                        : new OverlayCueAdmissionOutcome.Missing()
                );
            }
            transport.Stop(completed.Target, runId);
            Advance(identity, state);
            return Task.FromResult<OverlayCueAdmissionOutcome>(
                new OverlayCueAdmissionOutcome.ParentDisabledOrCancelled()
            );
        }
    }

    public OverlayCueAdmissionOutcome ReadOutcome(int hostId, Guid targetOverlayId, Guid runId)
    {
        var identity = new OverlayTargetIdentity(hostId, targetOverlayId);
        if (!_targets.TryGetValue(identity, out var state))
        {
            return new OverlayCueAdmissionOutcome.Missing();
        }
        lock (state.Gate)
        {
            if (state.Active.ContainsKey(runId))
            {
                return new OverlayCueAdmissionOutcome.Running(runId);
            }
            var pending = state.Pending.FirstOrDefault(value => value.Plan.RunId == runId);
            if (pending is not null)
            {
                return new OverlayCueAdmissionOutcome.Queued(runId);
            }
            if (state.Expired.Contains(runId))
            {
                return new OverlayCueAdmissionOutcome.Expired();
            }
            return state.Cancelled.Contains(runId)
                ? new OverlayCueAdmissionOutcome.ParentDisabledOrCancelled()
                : new OverlayCueAdmissionOutcome.Missing();
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _changesSubscription = events.Subscribe(
            [AppEventKind.OverlaysChanged, AppEventKind.HostedChannelsChanged],
            ObserverIdentity.For(typeof(OverlayCuePlaybackService)),
            (_, _) =>
            {
                _ = ValidateAllAsync(_stopping.Token);
                return ValueTask.CompletedTask;
            }
        );
        _monitor = MonitorAsync(_stopping.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _changesSubscription?.Dispose();
        _changesSubscription = null;
        _stopping.Cancel();
        if (_monitor is not null)
        {
            await _monitor.WaitAsync(cancellationToken);
        }
        foreach (var pair in _targets)
        {
            lock (pair.Value.Gate)
            {
                CancelAll(pair.Key, pair.Value);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!_stopping.IsCancellationRequested)
        {
            await StopAsync(CancellationToken.None);
        }
        _stopping.Dispose();
    }

    private async Task<PlanResolution> ResolvePlanAsync(
        OverlayCueAdmissionRequest request,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var references = await ResolveReferencesAsync(
            db,
            new(request.HostId, request.TargetOverlayId, request.CueId),
            cancellationToken
        );
        if (references is not ReferenceResolution.Available available)
        {
            return references switch
            {
                ReferenceResolution.Disabled { Part: OverlayCueReferencePart.Parent }
                or ReferenceResolution.Missing { Part: OverlayCueReferencePart.Parent } =>
                    new PlanResolution.ParentDisabled(),
                ReferenceResolution.Disabled => new PlanResolution.Disabled(),
                _ => new PlanResolution.Missing(),
            };
        }
        var target = available.Target;
        var cue = available.Cue;

        var parsed = OverlayCueConfiguration.Parse(cue.ConfigurationJson);
        if (parsed is not OverlayCueConfigurationResult.Valid valid)
        {
            logger.LogError(
                "Cue {CueId} for host {HostId} has an invalid persisted configuration.",
                cue.PublicId,
                cue.HostId
            );
            return new PlanResolution.Missing();
        }
        var assetIds = valid.Value.ReferencedAssetIds;
        var assets = await db
            .OverlayMediaAssets.AsNoTracking()
            .Where(value => value.HostId == request.HostId && assetIds.Contains(value.PublicId))
            .ToDictionaryAsync(value => value.PublicId, cancellationToken);
        if (assets.Count != assetIds.Length)
        {
            return new PlanResolution.Missing();
        }

        foreach (
            var url in valid
                .Value.Layers.Select(layer =>
                    layer switch
                    {
                        OverlayCueLayer.RemoteMedia remote => remote.Url,
                        OverlayCueLayer.ExternalWeb web => web.Url,
                        _ => null,
                    }
                )
                .OfType<Uri>()
        )
        {
            if (
                await urlPolicy.ValidateAsync(url, cancellationToken)
                is OverlayRemoteUrlDecision.Rejected
            )
            {
                return new PlanResolution.Disabled();
            }
        }

        var layers = valid
            .Value.Layers.Select(layer => ResolveLayer(layer, assets))
            .ToImmutableArray();
        var plan = new OverlayCuePlaybackPlan(
            Guid.NewGuid(),
            request.HostId,
            request.TargetOverlayId,
            request.CueId,
            cue.Revision,
            cue.DurationMilliseconds,
            request.Origin,
            request.Context,
            layers
        );
        return new PlanResolution.Ready(
            new ResolvedOverlayInstance(
                target.HostId,
                target.PublicId,
                target.Type,
                OverlayConfiguration.FromPersistence(target.Type, target.ConfigurationJson),
                new OverlayRevision(target.Revision)
            ),
            plan
        );
    }

    private static async Task<ReferenceResolution> ResolveReferencesAsync(
        BlokeBotDbContext db,
        OverlayCueReferenceRequest request,
        CancellationToken cancellationToken
    )
    {
        if (request.HostId <= 0)
        {
            return new ReferenceResolution.Missing(OverlayCueReferencePart.Parent);
        }
        if (request.TargetOverlayId == Guid.Empty)
        {
            return new ReferenceResolution.Missing(OverlayCueReferencePart.Target);
        }
        if (request.CueId == Guid.Empty)
        {
            return new ReferenceResolution.Missing(OverlayCueReferencePart.Cue);
        }

        var features = await db
            .Hosts.AsNoTracking()
            .Where(host => host.Id == request.HostId)
            .Select(host => (HostFeatureFlags?)host.EnabledFeatures)
            .SingleOrDefaultAsync(cancellationToken);
        if (features is null)
        {
            return new ReferenceResolution.Missing(OverlayCueReferencePart.Parent);
        }
        if ((features.Value & HostFeatureFlags.Overlays) != HostFeatureFlags.Overlays)
        {
            return new ReferenceResolution.Disabled(OverlayCueReferencePart.Parent);
        }

        var target = await db
            .OverlayInstances.AsNoTracking()
            .SingleOrDefaultAsync(
                value =>
                    value.HostId == request.HostId
                    && value.PublicId == request.TargetOverlayId
                    && value.Type == OverlayType.CuePlayer,
                cancellationToken
            );
        if (target is null)
        {
            return new ReferenceResolution.Missing(OverlayCueReferencePart.Target);
        }
        if (!target.IsEnabled)
        {
            return new ReferenceResolution.Disabled(OverlayCueReferencePart.Target);
        }

        var cue = await db
            .OverlayCues.AsNoTracking()
            .SingleOrDefaultAsync(
                value => value.HostId == request.HostId && value.PublicId == request.CueId,
                cancellationToken
            );
        if (cue is null)
        {
            return new ReferenceResolution.Missing(OverlayCueReferencePart.Cue);
        }
        return cue.IsEnabled
            ? new ReferenceResolution.Available(target, cue)
            : new ReferenceResolution.Disabled(OverlayCueReferencePart.Cue);
    }

    private static OverlayCuePlaybackLayer ResolveLayer(
        OverlayCueLayer layer,
        IReadOnlyDictionary<Guid, OverlayMediaAsset> assets
    ) =>
        layer switch
        {
            OverlayCueLayer.UploadedMedia uploaded => new OverlayCuePlaybackLayer.UploadedMedia
            {
                AssetId = uploaded.AssetId,
                ContentRevision = assets[uploaded.AssetId].ContentRevision,
                ContentType = assets[uploaded.AssetId].ContentType,
                Volume = uploaded.Volume,
                Fit = uploaded.Fit,
                Rectangle = uploaded.Rectangle,
                StartOffsetMilliseconds = uploaded.StartOffsetMilliseconds,
                DurationMilliseconds = uploaded.DurationMilliseconds,
                ZIndex = uploaded.ZIndex,
            },
            OverlayCueLayer.RemoteMedia remote => new OverlayCuePlaybackLayer.RemoteMedia
            {
                Url = remote.Url,
                MediaKind = remote.MediaKind,
                Volume = remote.Volume,
                Fit = remote.Fit,
                Rectangle = remote.Rectangle,
                StartOffsetMilliseconds = remote.StartOffsetMilliseconds,
                DurationMilliseconds = remote.DurationMilliseconds,
                ZIndex = remote.ZIndex,
            },
            OverlayCueLayer.ExternalWeb web => new OverlayCuePlaybackLayer.ExternalWeb
            {
                Url = web.Url,
                Rectangle = web.Rectangle,
                StartOffsetMilliseconds = web.StartOffsetMilliseconds,
                DurationMilliseconds = web.DurationMilliseconds,
                ZIndex = web.ZIndex,
            },
            _ => throw new InvalidOperationException("Unsupported Cue-V1 layer."),
        };

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
            .Where(asset => asset.HostId == identity.HostId && assetIds.Contains(asset.PublicId))
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
            state.Active.Remove(expired.Plan.RunId);
            state.Expired.Add(expired.Plan.RunId);
            transport.Stop(expired.Target, expired.Plan.RunId);
        }
        while (state.Pending.TryPeek(out var pending) && pending.ExpiresAtUtc <= now)
        {
            state.Pending.Dequeue();
            state.Expired.Add(pending.Plan.RunId);
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
            state.Pending.Dequeue();
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
            state.Cancelled.Add(active.Plan.RunId);
            transport.Stop(active.Target, active.Plan.RunId);
        }
        foreach (var pending in state.Pending)
        {
            state.Cancelled.Add(pending.Plan.RunId);
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

    private readonly record struct OverlayTargetIdentity(int HostId, Guid OverlayId);

    private sealed class TargetState
    {
        internal object Gate { get; } = new();

        internal Dictionary<Guid, AdmittedRun> Active { get; } = [];

        internal Queue<AdmittedRun> Pending { get; } = [];

        internal HashSet<Guid> Expired { get; } = [];

        internal HashSet<Guid> Cancelled { get; } = [];
    }

    private sealed record AdmittedRun(
        ResolvedOverlayInstance Target,
        OverlayCuePlaybackPlan Plan,
        OverlayCueQueuePolicy QueuePolicy,
        DateTimeOffset ExpiresAtUtc,
        DateTimeOffset? StartedAtUtc = null
    );

    private abstract record PlanResolution
    {
        private PlanResolution() { }

        internal sealed record Ready(ResolvedOverlayInstance Target, OverlayCuePlaybackPlan Plan)
            : PlanResolution;

        internal sealed record Missing : PlanResolution;

        internal sealed record Disabled : PlanResolution;

        internal sealed record ParentDisabled : PlanResolution;
    }

    private abstract record ReferenceResolution
    {
        private ReferenceResolution() { }

        internal sealed record Available(OverlayInstance Target, OverlayCue Cue)
            : ReferenceResolution;

        internal sealed record Missing(OverlayCueReferencePart Part) : ReferenceResolution;

        internal sealed record Disabled(OverlayCueReferencePart Part) : ReferenceResolution;
    }
}
