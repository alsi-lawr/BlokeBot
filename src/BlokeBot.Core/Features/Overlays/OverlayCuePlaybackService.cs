using System.Collections.Concurrent;
using System.Collections.Immutable;
using BlokeBot.Eventing;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BlokeBot.Core.Features.Overlays;

internal sealed partial class OverlayCuePlaybackService(
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
}
