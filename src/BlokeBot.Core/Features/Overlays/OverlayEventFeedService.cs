using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Overlays;

internal sealed partial class OverlayEventFeedService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    TimeProvider timeProvider,
    IServiceProvider services,
    ILogger<OverlayEventFeedService> logger
) : IOverlayEventPresenter, IHostFeatureActivationObserver, IHostedService, IAsyncDisposable
{
    private static readonly TimeSpan _tombstoneRetention = TimeSpan.FromHours(24);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _stopping = new();
    private Task? _scheduler;
    private int _disposeState;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _scheduler = RunSchedulerAsync(_stopping.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _stopping.Cancel();
        if (_scheduler is not null)
        {
            try
            {
                await _scheduler.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (_stopping.IsCancellationRequested) { }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }
        _stopping.Cancel();
        if (_scheduler is not null)
        {
            try
            {
                await _scheduler;
            }
            catch (OperationCanceledException) { }
        }
        _stopping.Dispose();
        _gate.Dispose();
    }

    private async Task RunSchedulerAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), timeProvider, cancellationToken);
            await AdvanceDueAsync(cancellationToken);
        }
    }

    private async Task AdvanceDueAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var now = timeProvider.GetUtcNow().UtcDateTime;
            _ = await db
                .OverlayEventFeedItems.Where(x =>
                    x.TombstoneExpiresAtUtc != null && x.TombstoneExpiresAtUtc <= now
                )
                .ExecuteDeleteAsync(cancellationToken);
            var candidateOverlayIds = await db
                .OverlayEventFeedItems.AsNoTracking()
                .Where(x =>
                    x.Lifecycle == OverlayEventFeedLifecycle.Active
                    || x.Lifecycle == OverlayEventFeedLifecycle.Queued
                )
                .Where(x => x.OverlayInstanceId != null)
                .Select(x => x.OverlayInstanceId!.Value)
                .Distinct()
                .ToArrayAsync(cancellationToken);
            var fullOwners = await ReconcileFullAsync(db, null, cancellationToken);
            foreach (var full in fullOwners)
            {
                if (await PruneAndAdvanceAsync(db, full, cancellationToken))
                {
                    await PublishStateAsync(full);
                }
            }
            var overlays = await db
                .OverlayInstances.Where(x => candidateOverlayIds.Contains(x.Id))
                .ToListAsync(cancellationToken);
            foreach (var overlay in overlays)
            {
                var configuration = (OverlayConfiguration.EventFeedV1)
                    OverlayConfiguration.FromPersistence(overlay.Type, overlay.ConfigurationJson);
                if (await PruneAndAdvanceAsync(db, overlay, cancellationToken))
                {
                    services
                        .GetRequiredService<IOverlayLivePublisher>()
                        .PublishState(ToResolved(overlay, configuration));
                }
            }
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    public async Task PresentAsync(
        OverlayEventPresentation presentation,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(presentation);
        if (
            presentation.HostId <= 0
            || string.IsNullOrWhiteSpace(presentation.SourceKey)
            || presentation.SourceKey.Length > 160
        )
        {
            throw new ArgumentException(
                "A host and stable source key are required.",
                nameof(presentation)
            );
        }
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await AdmitAsync(presentation, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                exception,
                "Event Feed admission failed for host {HostId} and kind {Kind}.",
                presentation.HostId,
                presentation.Kind
            );
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    public async ValueTask<HostFeatureAutomaticWorkResult> ApplyAsync(
        HostFeatureActivationChange change,
        CancellationToken cancellationToken
    )
    {
        if (change.State is HostFeatureActivationState.Enabled)
        {
            return new HostFeatureAutomaticWorkResult.Complete();
        }
        if (
            change.Feature
            is HostFeatureFlags.Points
                or HostFeatureFlags.Guessing
                or HostFeatureFlags.CommunityProgression
        )
        {
            await SuppressSourceAsync(change.HostId, change.Feature, cancellationToken);
            return new HostFeatureAutomaticWorkResult.Complete();
        }
        if (change.Feature is HostFeatureFlags.Overlays)
        {
            await SuppressAllAsync(change.HostId, cancellationToken);
        }

        return new HostFeatureAutomaticWorkResult.Complete();
    }

    private async Task PublishSuppressedStatesAsync(
        BlokeBotDbContext db,
        int hostId,
        IReadOnlyCollection<long> affectedOverlayIds,
        CancellationToken ct
    )
    {
        if (affectedOverlayIds.Count == 0)
        {
            return;
        }
        var overlays = await db
            .OverlayInstances.Where(x =>
                x.HostId == hostId
                && x.Type == OverlayType.EventFeed
                && affectedOverlayIds.Contains(x.Id)
            )
            .OrderBy(x => x.Id)
            .ToArrayAsync(ct);
        var publisher = services.GetRequiredService<IOverlayEventFeedLivePublisher>();
        foreach (var overlay in overlays)
        {
            var configuration = (OverlayConfiguration.EventFeedV1)
                OverlayConfiguration.FromPersistence(overlay.Type, overlay.ConfigurationJson);
            _ = await PruneAndAdvanceAsync(db, overlay, ct);
            publisher.PublishSuppression(
                ToResolved(overlay, configuration),
                await ReadStateAsync(db, hostId, overlay.Id, ct)
            );
        }
    }

    private async Task SuppressAsync(
        BlokeBotDbContext db,
        ResolvedOverlayInstance instance,
        OverlayEventFeedKind? kind,
        CancellationToken ct
    )
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var overlayId = await db
            .OverlayInstances.Where(x =>
                x.HostId == instance.HostId && x.PublicId == instance.OverlayId
            )
            .Select(x => x.Id)
            .SingleAsync(ct);
        var query = db.OverlayEventFeedItems.Where(x =>
            x.OverlayInstanceId == overlayId
            && (
                x.Lifecycle == OverlayEventFeedLifecycle.Active
                || x.Lifecycle == OverlayEventFeedLifecycle.Queued
            )
        );
        if (kind is { } value)
        {
            query = query.Where(x => x.Kind == value);
        }
        _ = await query.ExecuteUpdateAsync(
            setters =>
                setters
                    .SetProperty(x => x.Lifecycle, OverlayEventFeedLifecycle.Suppressed)
                    .SetProperty(x => x.DisplayDeadlineUtc, (DateTime?)null)
                    .SetProperty(x => x.TombstoneExpiresAtUtc, now.Add(_tombstoneRetention)),
            ct
        );
    }

    private static async Task<bool> RequiredFeaturesEnabledAsync(
        BlokeBotDbContext db,
        int hostId,
        HostFeatureFlags flags,
        CancellationToken ct
    ) =>
        await db
            .Hosts.AsNoTracking()
            .AnyAsync(x => x.Id == hostId && (x.EnabledFeatures & flags) == flags, ct);

    private static HostFeatureFlags SourceFeature(OverlayEventFeedKind kind) =>
        kind switch
        {
            OverlayEventFeedKind.PointAward or OverlayEventFeedKind.GiveawayWinner =>
                HostFeatureFlags.Points,
            OverlayEventFeedKind.GuessingWinner => HostFeatureFlags.Guessing,
            OverlayEventFeedKind.BingoEvent => HostFeatureFlags.Bingo,
            OverlayEventFeedKind.AchievementCompletion => HostFeatureFlags.CommunityProgression,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    private static string Title(OverlayEventFeedKind kind) =>
        kind switch
        {
            OverlayEventFeedKind.PointAward => "Points awarded",
            OverlayEventFeedKind.GuessingWinner => "Guessing winner",
            OverlayEventFeedKind.GiveawayWinner => "Giveaway winner",
            OverlayEventFeedKind.BingoEvent => "Bingo",
            OverlayEventFeedKind.AchievementCompletion => "Achievement unlocked",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    private static EventFeedCardPresentation ToPresentation(OverlayEventFeedItem item) =>
        new(
            item.Id,
            PersistedEnumTokens<OverlayEventFeedKind>.Format(item.Kind),
            PersistedEnumTokens<OverlayEventFeedPriority>.Format(item.Priority),
            EventFeedProjectionText.DecodeOnce(item.Title),
            EventFeedProjectionText.DecodeOnce(item.Body),
            new DateTimeOffset(item.EnqueuedAtUtc, TimeSpan.Zero),
            item.DisplayDeadlineUtc is { } deadline
                ? new DateTimeOffset(deadline, TimeSpan.Zero)
                : null
        );

    private static ResolvedOverlayInstance ToResolved(
        OverlayInstance overlay,
        OverlayConfiguration configuration
    ) =>
        new(
            overlay.HostId,
            overlay.PublicId,
            overlay.Type,
            configuration,
            new OverlayRevision(overlay.Revision)
        );
}
