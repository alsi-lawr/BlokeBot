using System.Collections.Immutable;
using System.Globalization;
using System.Net;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Overlays;

public abstract record OverlayEventPresentation
{
    private OverlayEventPresentation() { }

    public abstract OverlayEventFeedKind Kind { get; }
    public required int HostId { get; init; }
    public required string SourceKey { get; init; }

    public sealed record PointAward : OverlayEventPresentation
    {
        public override OverlayEventFeedKind Kind => OverlayEventFeedKind.PointAward;
        public required string Recipient { get; init; }
        public required string Amount { get; init; }
        public required string PointLabel { get; init; }
    }

    public sealed record GuessingWinner : OverlayEventPresentation
    {
        public override OverlayEventFeedKind Kind => OverlayEventFeedKind.GuessingWinner;
        public required string RoundName { get; init; }
        public required string WinningAnswer { get; init; }
        public ImmutableArray<string> Winners { get; init; } = [];
        public required string Amount { get; init; }
        public required string PointLabel { get; init; }
    }

    public sealed record GiveawayWinner : OverlayEventPresentation
    {
        public override OverlayEventFeedKind Kind => OverlayEventFeedKind.GiveawayWinner;
        public ImmutableArray<string> Winners { get; init; } = [];
        public ImmutableArray<string> Prizes { get; init; } = [];
        public required string PointLabel { get; init; }
    }

    public sealed record BingoEvent : OverlayEventPresentation
    {
        public override OverlayEventFeedKind Kind => OverlayEventFeedKind.BingoEvent;
        public required string Summary { get; init; }
    }
}

public interface IOverlayEventPresenter
{
    Task PresentAsync(OverlayEventPresentation presentation, CancellationToken cancellationToken);
}

public sealed record EventFeedCardPresentation(
    long Id,
    string Kind,
    string Priority,
    string Title,
    string Body,
    DateTimeOffset EnqueuedAtUtc,
    DateTimeOffset? DisplayDeadlineUtc
);

public sealed record EventFeedStatePresentation(
    EventFeedCardPresentation? Active,
    IReadOnlyList<EventFeedCardPresentation> Pending
);

internal sealed partial class EventFeedTemplateRenderer
{
    [GeneratedRegex("\\{([A-Za-z][A-Za-z0-9]*)\\}", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderPattern();

    internal static string Render(
        EventFeedKindConfiguration configuration,
        OverlayEventPresentation presentation
    )
    {
        var values = Values(presentation);
        ValidateTemplate(configuration.Template, presentation.Kind, values);
        var rendered = PlaceholderPattern()
            .Replace(
                configuration.Template,
                match =>
                {
                    var name = match.Groups[1].Value;
                    return values[name];
                }
            );
        return HtmlEncoder.Default.Encode(rendered);
    }

    private static void ValidateTemplate(
        string template,
        OverlayEventFeedKind kind,
        IReadOnlyDictionary<string, string> values
    )
    {
        var withoutKnownPlaceholders = PlaceholderPattern()
            .Replace(
                template,
                match =>
                {
                    var name = match.Groups[1].Value;
                    return !values.ContainsKey(name)
                        ? throw new ArgumentException(
                            $"Placeholder {{{name}}} is not valid for {kind}."
                        )
                        : string.Empty;
                }
            );
        if (
            withoutKnownPlaceholders.Contains('{', StringComparison.Ordinal)
            || withoutKnownPlaceholders.Contains('}', StringComparison.Ordinal)
        )
        {
            throw new ArgumentException("Templates may contain only known placeholders.");
        }
    }

    private static IReadOnlyDictionary<string, string> Values(
        OverlayEventPresentation presentation
    ) =>
        presentation switch
        {
            OverlayEventPresentation.PointAward point => new Dictionary<string, string>(
                StringComparer.Ordinal
            )
            {
                ["recipient"] = point.Recipient,
                ["amount"] = point.Amount,
                ["pointLabel"] = point.PointLabel,
            },
            OverlayEventPresentation.GuessingWinner guessing => new Dictionary<string, string>(
                StringComparer.Ordinal
            )
            {
                ["roundName"] = guessing.RoundName,
                ["winningAnswer"] = guessing.WinningAnswer,
                ["winners"] = Join(guessing.Winners),
                ["winnerCount"] = guessing.Winners.Length.ToString(CultureInfo.InvariantCulture),
                ["amount"] = guessing.Amount,
                ["pointLabel"] = guessing.PointLabel,
            },
            OverlayEventPresentation.GiveawayWinner giveaway => new Dictionary<string, string>(
                StringComparer.Ordinal
            )
            {
                ["winners"] = Join(giveaway.Winners),
                ["winnerCount"] = giveaway.Winners.Length.ToString(CultureInfo.InvariantCulture),
                ["prizes"] = Join(giveaway.Prizes),
                ["pointLabel"] = giveaway.PointLabel,
            },
            OverlayEventPresentation.BingoEvent bingo => new Dictionary<string, string>(
                StringComparer.Ordinal
            )
            {
                ["summary"] = bingo.Summary,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(presentation)),
        };

    private static string Join(IEnumerable<string> values) => string.Join(", ", values);
}

internal static class EventFeedProjectionText
{
    internal static string DecodeOnce(string durableText) => WebUtility.HtmlDecode(durableText);
}

internal sealed class OverlayEventFeedService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    TimeProvider timeProvider,
    IServiceProvider services,
    ILogger<OverlayEventFeedService> logger
) : IOverlayEventPresenter, IHostFeatureChangeObserver, IHostedService, IAsyncDisposable
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
                .Select(x => x.OverlayInstanceId)
                .Distinct()
                .ToArrayAsync(cancellationToken);
            if (candidateOverlayIds.Length == 0)
            {
                return;
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

    public async ValueTask FeatureChangedAsync(
        int hostId,
        HostFeatureFlags feature,
        bool enabled,
        CancellationToken cancellationToken
    )
    {
        if (enabled)
        {
            return;
        }
        if (feature is HostFeatureFlags.Points or HostFeatureFlags.Guessing)
        {
            await SuppressSourceAsync(hostId, feature, cancellationToken);
            return;
        }
        if (feature is HostFeatureFlags.Overlays)
        {
            await SuppressAllAsync(hostId, cancellationToken);
        }
    }

    private async Task SuppressAllAsync(int hostId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var now = timeProvider.GetUtcNow().UtcDateTime;
            _ = await db
                .OverlayEventFeedItems.Where(x =>
                    x.HostId == hostId
                    && (
                        x.Lifecycle == OverlayEventFeedLifecycle.Active
                        || x.Lifecycle == OverlayEventFeedLifecycle.Queued
                    )
                )
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(x => x.Lifecycle, OverlayEventFeedLifecycle.Suppressed)
                            .SetProperty(x => x.DisplayDeadlineUtc, (DateTime?)null)
                            .SetProperty(
                                x => x.TombstoneExpiresAtUtc,
                                now.Add(_tombstoneRetention)
                            ),
                    cancellationToken
                );
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    internal async Task<EventFeedStatePresentation?> ReadAsync(
        ResolvedOverlayInstance instance,
        CancellationToken cancellationToken
    )
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            if (
                !await RequiredFeaturesEnabledAsync(
                    db,
                    instance.HostId,
                    HostFeatureFlags.Overlays,
                    cancellationToken
                )
            )
            {
                await SuppressAsync(db, instance, null, cancellationToken);
                return null;
            }
            await AdvanceAsync(db, instance, cancellationToken);
            var overlayId = await db
                .OverlayInstances.Where(x =>
                    x.HostId == instance.HostId && x.PublicId == instance.OverlayId
                )
                .Select(x => x.Id)
                .SingleAsync(cancellationToken);
            var items = await db
                .OverlayEventFeedItems.AsNoTracking()
                .Where(x =>
                    x.HostId == instance.HostId
                    && x.OverlayInstanceId == overlayId
                    && (
                        x.Lifecycle == OverlayEventFeedLifecycle.Active
                        || x.Lifecycle == OverlayEventFeedLifecycle.Queued
                    )
                )
                .OrderBy(x => x.EnqueuedAtUtc)
                .ThenBy(x => x.Id)
                .ToListAsync(cancellationToken);
            return new EventFeedStatePresentation(
                items
                    .Where(x => x.Lifecycle == OverlayEventFeedLifecycle.Active)
                    .Select(ToPresentation)
                    .SingleOrDefault(),
                items
                    .Where(x => x.Lifecycle == OverlayEventFeedLifecycle.Queued)
                    .Select(ToPresentation)
                    .ToArray()
            );
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    internal async Task SuppressSourceAsync(
        int hostId,
        HostFeatureFlags source,
        CancellationToken cancellationToken
    )
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var kind = source switch
            {
                HostFeatureFlags.Guessing => OverlayEventFeedKind.GuessingWinner,
                HostFeatureFlags.Points => (OverlayEventFeedKind?)null,
                HostFeatureFlags.Bingo => OverlayEventFeedKind.BingoEvent,
                _ => null,
            };
            var query = db.OverlayEventFeedItems.Where(x =>
                x.HostId == hostId
                && (
                    x.Lifecycle == OverlayEventFeedLifecycle.Active
                    || x.Lifecycle == OverlayEventFeedLifecycle.Queued
                )
            );
            if (kind is { } exact)
            {
                query = query.Where(x => x.Kind == exact);
            }
            else if (source == HostFeatureFlags.Points)
            {
                query = query.Where(x =>
                    x.Kind == OverlayEventFeedKind.PointAward
                    || x.Kind == OverlayEventFeedKind.GiveawayWinner
                );
            }
            else
            {
                return;
            }
            var now = timeProvider.GetUtcNow().UtcDateTime;
            _ = await query.ExecuteUpdateAsync(
                setters =>
                    setters
                        .SetProperty(x => x.Lifecycle, OverlayEventFeedLifecycle.Suppressed)
                        .SetProperty(x => x.DisplayDeadlineUtc, (DateTime?)null)
                        .SetProperty(x => x.TombstoneExpiresAtUtc, now.Add(_tombstoneRetention)),
                cancellationToken
            );
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    private async Task AdmitAsync(OverlayEventPresentation presentation, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var required = SourceFeature(presentation.Kind) | HostFeatureFlags.Overlays;
        if (!await RequiredFeaturesEnabledAsync(db, presentation.HostId, required, ct))
        {
            return;
        }
        var overlays = await db
            .OverlayInstances.Where(x =>
                x.HostId == presentation.HostId && x.Type == OverlayType.EventFeed && x.IsEnabled
            )
            .OrderBy(x => x.Id)
            .ToListAsync(ct);
        foreach (var overlay in overlays)
        {
            var configuration = (OverlayConfiguration.EventFeedV1)
                OverlayConfiguration.FromPersistence(overlay.Type, overlay.ConfigurationJson);
            var kindConfiguration = configuration.Kinds[presentation.Kind];
            if (!kindConfiguration.Enabled)
            {
                continue;
            }
            _ = await PruneAndAdvanceAsync(db, overlay, ct);
            if (
                await db.OverlayEventFeedItems.AnyAsync(
                    x =>
                        x.OverlayInstanceId == overlay.Id
                        && x.Kind == presentation.Kind
                        && x.SourceKey == presentation.SourceKey,
                    ct
                )
            )
            {
                continue;
            }
            var count = await db.OverlayEventFeedItems.CountAsync(
                x =>
                    x.OverlayInstanceId == overlay.Id
                    && (
                        x.Lifecycle == OverlayEventFeedLifecycle.Active
                        || x.Lifecycle == OverlayEventFeedLifecycle.Queued
                    ),
                ct
            );
            if (count >= configuration.Capacity)
            {
                if (configuration.OverflowPolicy == EventFeedOverflowPolicy.DropNewest)
                {
                    continue;
                }
                var replaced = await db
                    .OverlayEventFeedItems.Where(x =>
                        x.OverlayInstanceId == overlay.Id
                        && x.Kind == presentation.Kind
                        && x.Lifecycle == OverlayEventFeedLifecycle.Queued
                    )
                    .OrderByDescending(x => x.EnqueuedAtUtc)
                    .ThenByDescending(x => x.Id)
                    .FirstOrDefaultAsync(ct);
                if (replaced is null)
                {
                    continue;
                }
                replaced.Lifecycle = OverlayEventFeedLifecycle.Suppressed;
                replaced.TombstoneExpiresAtUtc = timeProvider
                    .GetUtcNow()
                    .UtcDateTime.Add(_tombstoneRetention);
            }
            var now = timeProvider.GetUtcNow().UtcDateTime;
            var hasActive = await db.OverlayEventFeedItems.AnyAsync(
                x =>
                    x.OverlayInstanceId == overlay.Id
                    && x.Lifecycle == OverlayEventFeedLifecycle.Active,
                ct
            );
            _ = db.OverlayEventFeedItems.Add(
                new OverlayEventFeedItem
                {
                    OverlayInstanceId = overlay.Id,
                    HostId = overlay.HostId,
                    Kind = presentation.Kind,
                    SourceKey = presentation.SourceKey,
                    Priority = kindConfiguration.Priority,
                    Lifecycle = hasActive
                        ? OverlayEventFeedLifecycle.Queued
                        : OverlayEventFeedLifecycle.Active,
                    Title = Title(presentation.Kind),
                    Body = EventFeedTemplateRenderer.Render(kindConfiguration, presentation),
                    DurationSeconds = kindConfiguration.DurationSeconds,
                    EnqueuedAtUtc = now,
                    DisplayDeadlineUtc = hasActive
                        ? null
                        : now.AddSeconds(kindConfiguration.DurationSeconds),
                }
            );
            _ = await db.SaveChangesAsync(ct);
            services
                .GetRequiredService<IOverlayLivePublisher>()
                .PublishState(ToResolved(overlay, configuration));
        }
    }

    private async Task AdvanceAsync(
        BlokeBotDbContext db,
        ResolvedOverlayInstance instance,
        CancellationToken ct
    )
    {
        var overlay = await db.OverlayInstances.SingleAsync(
            x => x.PublicId == instance.OverlayId && x.HostId == instance.HostId,
            ct
        );
        _ = await PruneAndAdvanceAsync(db, overlay, ct);
    }

    private async Task<bool> PruneAndAdvanceAsync(
        BlokeBotDbContext db,
        OverlayInstance overlay,
        CancellationToken ct
    )
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        _ = await db
            .OverlayEventFeedItems.Where(x =>
                x.OverlayInstanceId == overlay.Id
                && x.TombstoneExpiresAtUtc != null
                && x.TombstoneExpiresAtUtc <= now
            )
            .ExecuteDeleteAsync(ct);
        var changed = await RecoverUnavailableSourcesAsync(db, overlay, now, ct) > 0;
        var active = await db.OverlayEventFeedItems.SingleOrDefaultAsync(
            x =>
                x.OverlayInstanceId == overlay.Id
                && x.Lifecycle == OverlayEventFeedLifecycle.Active,
            ct
        );
        if (active is not null && active.DisplayDeadlineUtc > now)
        {
            return changed;
        }
        if (active is not null)
        {
            active.Lifecycle = OverlayEventFeedLifecycle.Consumed;
            active.DisplayDeadlineUtc = null;
            active.TombstoneExpiresAtUtc = now.Add(_tombstoneRetention);
            _ = await db.SaveChangesAsync(ct);
            changed = true;
        }
        var queued = await db
            .OverlayEventFeedItems.Where(x =>
                x.OverlayInstanceId == overlay.Id && x.Lifecycle == OverlayEventFeedLifecycle.Queued
            )
            .OrderBy(x => x.EnqueuedAtUtc)
            .ThenBy(x => x.Id)
            .ToListAsync(ct);
        if (queued.Count == 0)
        {
            _ = await db.SaveChangesAsync(ct);
            return changed;
        }
        var recent = await db
            .OverlayEventFeedItems.AsNoTracking()
            .Where(x =>
                x.OverlayInstanceId == overlay.Id
                && x.Lifecycle == OverlayEventFeedLifecycle.Consumed
            )
            .OrderByDescending(x => x.TombstoneExpiresAtUtc)
            .ThenByDescending(x => x.Id)
            .Take(3)
            .Select(x => x.Priority)
            .ToListAsync(ct);
        var forceNormal =
            queued.Any(x => x.Priority == OverlayEventFeedPriority.Normal)
            && recent.Count == 3
            && recent.All(x => x == OverlayEventFeedPriority.High);
        var next =
            queued.FirstOrDefault(x =>
                x.Priority
                == (forceNormal ? OverlayEventFeedPriority.Normal : OverlayEventFeedPriority.High)
            ) ?? queued[0];
        next.Lifecycle = OverlayEventFeedLifecycle.Active;
        next.DisplayDeadlineUtc = now.AddSeconds(next.DurationSeconds);
        _ = await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task<int> RecoverUnavailableSourcesAsync(
        BlokeBotDbContext db,
        OverlayInstance overlay,
        DateTime now,
        CancellationToken ct
    )
    {
        var enabledFeatures = await db
            .Hosts.AsNoTracking()
            .Where(x => x.Id == overlay.HostId)
            .Select(x => x.EnabledFeatures)
            .SingleAsync(ct);
        var activeOrQueued = db.OverlayEventFeedItems.Where(x =>
            x.OverlayInstanceId == overlay.Id
            && (
                x.Lifecycle == OverlayEventFeedLifecycle.Active
                || x.Lifecycle == OverlayEventFeedLifecycle.Queued
            )
        );
        if ((enabledFeatures & HostFeatureFlags.Overlays) != HostFeatureFlags.Overlays)
        {
            return await SuppressRecoveredAsync(activeOrQueued, now, ct);
        }
        var suppressed = 0;
        if ((enabledFeatures & HostFeatureFlags.Points) != HostFeatureFlags.Points)
        {
            suppressed += await SuppressRecoveredAsync(
                activeOrQueued.Where(x =>
                    x.Kind == OverlayEventFeedKind.PointAward
                    || x.Kind == OverlayEventFeedKind.GiveawayWinner
                ),
                now,
                ct
            );
        }
        if ((enabledFeatures & HostFeatureFlags.Guessing) != HostFeatureFlags.Guessing)
        {
            suppressed += await SuppressRecoveredAsync(
                activeOrQueued.Where(x => x.Kind == OverlayEventFeedKind.GuessingWinner),
                now,
                ct
            );
        }
        if ((enabledFeatures & HostFeatureFlags.Bingo) != HostFeatureFlags.Bingo)
        {
            suppressed += await SuppressRecoveredAsync(
                activeOrQueued.Where(x => x.Kind == OverlayEventFeedKind.BingoEvent),
                now,
                ct
            );
        }
        return suppressed;
    }

    private static Task<int> SuppressRecoveredAsync(
        IQueryable<OverlayEventFeedItem> query,
        DateTime now,
        CancellationToken ct
    ) =>
        query.ExecuteUpdateAsync(
            setters =>
                setters
                    .SetProperty(x => x.Lifecycle, OverlayEventFeedLifecycle.Suppressed)
                    .SetProperty(x => x.DisplayDeadlineUtc, (DateTime?)null)
                    .SetProperty(x => x.TombstoneExpiresAtUtc, now.Add(_tombstoneRetention)),
            ct
        );

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
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    private static string Title(OverlayEventFeedKind kind) =>
        kind switch
        {
            OverlayEventFeedKind.PointAward => "Points awarded",
            OverlayEventFeedKind.GuessingWinner => "Guessing winner",
            OverlayEventFeedKind.GiveawayWinner => "Giveaway winner",
            OverlayEventFeedKind.BingoEvent => "Bingo",
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
