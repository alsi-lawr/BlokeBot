using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Eventing;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.CommunityProgression;

public sealed class CommunityProgressionService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    EventBus<AppEventKind> events,
    TimeProvider clock,
    IEnumerable<ICommunityProgressionChangeObserver>? changeObservers = null,
    IEnumerable<ICommunityAchievementCompletionObserver>? achievementObservers = null
) : ICommunityAchievementGrantService
{
    private const int _persistenceRetryCount = 20;
    private static readonly HostFeatureFlags _requiredFeature =
        HostFeatureFlags.CommunityProgression;
    private readonly ICommunityProgressionChangeObserver[] _changeObservers =
    [
        .. changeObservers ?? [],
    ];
    private readonly ICommunityAchievementCompletionObserver[] _achievementObservers =
    [
        .. achievementObservers ?? [],
    ];

    public async Task<CommunityOperationOutcome> CreateSeasonAsync(
        int hostId,
        CommunitySeasonDraft draft,
        CancellationToken ct
    )
    {
        if (Validate(draft) is { } invalid)
        {
            return invalid;
        }
        if (!await FeatureEnabledAsync(hostId, ct))
        {
            return new CommunityOperationOutcome.FeatureDisabled();
        }

        var result = await RetryAsync<CommunityOperationOutcome>(
            async () =>
            {
                await using var db = await dbFactory.CreateDbContextAsync(ct);
                var existing = await db.CommunitySeasons.SingleOrDefaultAsync(
                    value =>
                        value.HostId == hostId && value.CreationOperationId == draft.OperationId,
                    ct
                );
                if (existing is not null)
                {
                    return new CommunityOperationOutcome.Succeeded(true);
                }

                await using var transaction = await db.Database.BeginTransactionAsync(ct);
                if (!await FeatureEnabledAsync(db, hostId, ct))
                {
                    return new CommunityOperationOutcome.FeatureDisabled();
                }
                var now = clock.GetUtcNow().UtcDateTime;
                var actor = Normalize(draft.Actor);
                var season = new CommunitySeason
                {
                    PublicId = Guid.NewGuid(),
                    HostId = hostId,
                    CreationOperationId = draft.OperationId,
                    Name = draft.Name.Trim(),
                    Description = draft.Description.Trim(),
                    ModeratorNotes = draft.ModeratorNotes.Trim(),
                    Status = CommunitySeasonStatus.Draft,
                    Visibility = draft.Visibility,
                    StartsAtUtc = EnsureUtc(draft.StartsAtUtc),
                    EndsAtUtc = EnsureUtc(draft.EndsAtUtc),
                    Revision = 1,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                };
                _ = db.CommunitySeasons.Add(season);
                _ = await db.SaveChangesAsync(ct);
                _ = db.CommunityAudits.Add(
                    Audit(
                        hostId,
                        season,
                        null,
                        "SeasonCreated",
                        draft.OperationId.ToString("N"),
                        actor,
                        draft.ModeratorNotes,
                        now
                    )
                );
                _ = await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return new CommunityOperationOutcome.Succeeded();
            },
            ct
        );
        await PublishIfChangedAsync(hostId, result, pointsChanged: false, ct);
        return result;
    }

    public async Task<CommunityOperationOutcome> AddRewardAsync(
        int hostId,
        CommunityRewardDraft draft,
        CancellationToken ct
    )
    {
        if (Validate(draft) is { } invalid)
        {
            return invalid;
        }
        if (!await FeatureEnabledAsync(hostId, ct))
        {
            return new CommunityOperationOutcome.FeatureDisabled();
        }

        var result = await RetryAsync<CommunityOperationOutcome>(
            async () =>
            {
                await using var db = await dbFactory.CreateDbContextAsync(ct);
                await using var transaction = await db.Database.BeginTransactionAsync(ct);
                if (!await FeatureEnabledAsync(db, hostId, ct))
                {
                    return new CommunityOperationOutcome.FeatureDisabled();
                }
                var season = await db.CommunitySeasons.SingleOrDefaultAsync(
                    value => value.HostId == hostId && value.PublicId == draft.SeasonId.Value,
                    ct
                );
                if (season is null)
                {
                    return new CommunityOperationOutcome.NotFound();
                }
                if (season.Status != CommunitySeasonStatus.Draft)
                {
                    return new CommunityOperationOutcome.Conflict(
                        "Rewards can only be added to a draft season."
                    );
                }
                var operationKey = $"reward:{draft.OperationId:N}";
                if (
                    await db.CommunityAudits.AnyAsync(
                        value =>
                            value.HostId == hostId
                            && value.Action == "RewardAdded"
                            && value.OperationKey == operationKey,
                        ct
                    )
                )
                {
                    return new CommunityOperationOutcome.Succeeded(true);
                }
                var key = NormalizeKey(draft.Key);
                if (
                    await db.CommunityRewardDefinitions.AnyAsync(
                        value => value.HostId == hostId && value.Key == key,
                        ct
                    )
                )
                {
                    return new CommunityOperationOutcome.Conflict(
                        "A reward already uses that host-scoped key."
                    );
                }
                var now = clock.GetUtcNow().UtcDateTime;
                var reward = new CommunityRewardDefinition
                {
                    PublicId = Guid.NewGuid(),
                    HostId = hostId,
                    SeasonId = season.Id,
                    Key = key,
                    Kind = draft.Kind,
                    Name = draft.Name.Trim(),
                    PresentationToken = draft.PresentationToken.Trim().ToLowerInvariant(),
                    CreatedAtUtc = now,
                };
                _ = db.CommunityRewardDefinitions.Add(reward);
                _ = db.CommunityAudits.Add(
                    Audit(
                        hostId,
                        season,
                        null,
                        "RewardAdded",
                        operationKey,
                        Normalize(draft.Actor),
                        string.Empty,
                        now
                    )
                );
                _ = await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return new CommunityOperationOutcome.Succeeded();
            },
            ct
        );
        await PublishIfChangedAsync(hostId, result, pointsChanged: false, ct);
        return result;
    }

    public async Task<CommunityOperationOutcome> AddDefinitionAsync(
        int hostId,
        CommunityDefinitionDraft draft,
        CancellationToken ct
    )
    {
        if (Validate(draft) is { } invalid)
        {
            return invalid;
        }
        if (!await FeatureEnabledAsync(hostId, ct))
        {
            return new CommunityOperationOutcome.FeatureDisabled();
        }

        var result = await RetryAsync<CommunityOperationOutcome>(
            async () =>
            {
                await using var db = await dbFactory.CreateDbContextAsync(ct);
                await using var transaction = await db.Database.BeginTransactionAsync(ct);
                if (!await FeatureEnabledAsync(db, hostId, ct))
                {
                    return new CommunityOperationOutcome.FeatureDisabled();
                }
                var season = await db.CommunitySeasons.SingleOrDefaultAsync(
                    value => value.HostId == hostId && value.PublicId == draft.SeasonId.Value,
                    ct
                );
                if (season is null)
                {
                    return new CommunityOperationOutcome.NotFound();
                }
                if (season.Status != CommunitySeasonStatus.Draft)
                {
                    return new CommunityOperationOutcome.Conflict(
                        "Definitions can only be added to a draft season."
                    );
                }
                var operationKey = $"definition:{draft.OperationId:N}";
                if (
                    await db.CommunityAudits.AnyAsync(
                        value =>
                            value.HostId == hostId
                            && value.Action == "DefinitionAdded"
                            && value.OperationKey == operationKey,
                        ct
                    )
                )
                {
                    return new CommunityOperationOutcome.Succeeded(true);
                }
                var key = NormalizeKey(draft.Key);
                if (
                    await db.CommunityDefinitions.AnyAsync(
                        value => value.HostId == hostId && value.Key == key,
                        ct
                    )
                )
                {
                    return new CommunityOperationOutcome.Conflict(
                        "A definition already uses that host-scoped key."
                    );
                }
                var rewards = await db
                    .CommunityRewardDefinitions.Where(value =>
                        value.HostId == hostId
                        && value.SeasonId == season.Id
                        && draft.Rewards.Select(id => id.Value).Contains(value.PublicId)
                    )
                    .ToListAsync(ct);
                if (rewards.Count != draft.Rewards.Distinct().Count())
                {
                    return new CommunityOperationOutcome.Invalid(
                        "Every selected reward must belong to this host and season."
                    );
                }
                var now = clock.GetUtcNow().UtcDateTime;
                var definition = new CommunityDefinition
                {
                    PublicId = Guid.NewGuid(),
                    HostId = hostId,
                    SeasonId = season.Id,
                    Key = key,
                    Name = draft.Name.Trim(),
                    Description = draft.Description.Trim(),
                    Kind = draft.Kind,
                    Scope = draft.Scope,
                    CompletionMode = draft.CompletionMode,
                    EventRule = draft.EventRule,
                    Increment = draft.Increment,
                    FilterToken = NormalizeFilter(draft.FilterToken),
                    Target = draft.Target,
                    PointsReward = draft.PointsReward.ToString(),
                    ResetCadence = draft.ResetSchedule.Cadence,
                    ResetLocalTime = draft.ResetSchedule.LocalTime.ToString(
                        "HH:mm",
                        CultureInfo.InvariantCulture
                    ),
                    ResetWeekday = draft.ResetSchedule.Weekday is { } weekday ? (int)weekday : null,
                    ScheduleRevision = 1,
                    CreatedAtUtc = now,
                };
                _ = db.CommunityDefinitions.Add(definition);
                foreach (var reward in rewards)
                {
                    _ = db.CommunityDefinitionRewards.Add(
                        new() { Definition = definition, RewardDefinitionId = reward.Id }
                    );
                }
                _ = await db.SaveChangesAsync(ct);
                _ = db.CommunityAudits.Add(
                    Audit(
                        hostId,
                        season,
                        definition,
                        "DefinitionAdded",
                        operationKey,
                        Normalize(draft.Actor),
                        string.Empty,
                        now
                    )
                );
                _ = await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return new CommunityOperationOutcome.Succeeded();
            },
            ct
        );
        await PublishIfChangedAsync(hostId, result, pointsChanged: false, ct);
        return result;
    }

    public async Task<CommunityOperationOutcome> TransitionSeasonAsync(
        int hostId,
        CommunitySeasonTransitionCommand command,
        CancellationToken ct
    )
    {
        if (!await FeatureEnabledAsync(hostId, ct))
        {
            return new CommunityOperationOutcome.FeatureDisabled();
        }
        if (command.OperationId == Guid.Empty)
        {
            return new CommunityOperationOutcome.Invalid("An operation ID is required.");
        }
        if (command.Transition == CommunitySeasonTransition.Close)
        {
            await ReconcileCompletedBountyEventsAsync(ct);
        }

        var result = await RetryAsync<CommunityOperationOutcome>(
            async () =>
            {
                await using var db = await dbFactory.CreateDbContextAsync(ct);
                await using var transaction = await db.Database.BeginTransactionAsync(ct);
                if (!await FeatureEnabledAsync(db, hostId, ct))
                {
                    return new CommunityOperationOutcome.FeatureDisabled();
                }
                var action = command.Transition.ToString();
                var operationKey = command.OperationId.ToString("N");
                if (
                    await db.CommunityAudits.AnyAsync(
                        value =>
                            value.HostId == hostId
                            && value.Action == action
                            && value.OperationKey == operationKey,
                        ct
                    )
                )
                {
                    return new CommunityOperationOutcome.Succeeded(true);
                }
                var season = await db
                    .CommunitySeasons.Include(value => value.Definitions)
                    .SingleOrDefaultAsync(
                        value => value.HostId == hostId && value.PublicId == command.SeasonId.Value,
                        ct
                    );
                if (season is null)
                {
                    return new CommunityOperationOutcome.NotFound();
                }
                if (season.Revision != command.ExpectedRevision)
                {
                    return new CommunityOperationOutcome.Conflict(
                        $"The season changed at revision {season.Revision}."
                    );
                }
                var expected = command.Transition switch
                {
                    CommunitySeasonTransition.Open => CommunitySeasonStatus.Draft,
                    CommunitySeasonTransition.Close => CommunitySeasonStatus.Open,
                    CommunitySeasonTransition.Archive => CommunitySeasonStatus.Closed,
                    _ => throw new UnreachableException(),
                };
                if (season.Status != expected)
                {
                    return new CommunityOperationOutcome.Conflict(
                        $"A {season.Status} season cannot {action.ToLowerInvariant()}."
                    );
                }
                if (
                    command.Transition == CommunitySeasonTransition.Open
                    && season.Definitions.Count == 0
                )
                {
                    return new CommunityOperationOutcome.Conflict(
                        "Add at least one quest or achievement before opening the season."
                    );
                }
                if (
                    command.Transition == CommunitySeasonTransition.Close
                    && await HasUnreconciledBountyEventsAsync(db, season, ct)
                )
                {
                    return new CommunityOperationOutcome.Conflict(
                        "A completed bounty arrived while the season was closing. Retry to include it in the final standings."
                    );
                }
                var now = clock.GetUtcNow().UtcDateTime;
                switch (command.Transition)
                {
                    case CommunitySeasonTransition.Open:
                        season.Status = CommunitySeasonStatus.Open;
                        season.OpenedAtUtc = now;
                        foreach (
                            var definition in season.Definitions.Where(value =>
                                value.ResetCadence != CommunityResetCadence.None
                            )
                        )
                        {
                            _ = await EnsureCurrentPeriodAsync(
                                db,
                                season,
                                definition,
                                CommunityRolloverKind.Restart,
                                $"open:{operationKey}:{definition.Id}",
                                now,
                                ct
                            );
                        }
                        break;
                    case CommunitySeasonTransition.Close:
                        await SnapshotStandingsAsync(db, season, now, ct);
                        season.Status = CommunitySeasonStatus.Closed;
                        season.ClosedAtUtc = now;
                        break;
                    case CommunitySeasonTransition.Archive:
                        season.Status = CommunitySeasonStatus.Archived;
                        season.ArchivedAtUtc = now;
                        break;
                    default:
                        throw new UnreachableException();
                }
                season.Revision++;
                season.UpdatedAtUtc = now;
                _ = db.CommunityAudits.Add(
                    Audit(
                        hostId,
                        season,
                        null,
                        action,
                        operationKey,
                        Normalize(command.Actor),
                        command.PrivateNote,
                        now
                    )
                );
                _ = db.CommunityEvents.Add(
                    DomainEvent(
                        hostId,
                        season.Id,
                        command.Transition switch
                        {
                            CommunitySeasonTransition.Open => CommunityEventKind.SeasonOpened,
                            CommunitySeasonTransition.Close => CommunityEventKind.SeasonClosed,
                            CommunitySeasonTransition.Archive => CommunityEventKind.SeasonArchived,
                            _ => throw new UnreachableException(),
                        },
                        $"season:{action}:{operationKey}",
                        JsonSerializer.Serialize(new { season.PublicId, season.Name }),
                        now
                    )
                );
                _ = await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return new CommunityOperationOutcome.Succeeded();
            },
            ct
        );
        await PublishIfChangedAsync(hostId, result, pointsChanged: false, ct);
        return result;
    }

    public async Task<CommunityOperationOutcome> ProcessEventAsync(
        int hostId,
        CommunitySourceEvent sourceEvent,
        CancellationToken ct
    )
    {
        if (Validate(sourceEvent) is { } invalid)
        {
            return invalid;
        }
        if (!await FeatureEnabledAsync(hostId, ct))
        {
            return new CommunityOperationOutcome.FeatureDisabled();
        }

        var result = await RetryAsync<CommunityOperationOutcome>(
            () => ProcessEventAttemptAsync(hostId, sourceEvent, ct),
            ct
        );
        await PublishIfChangedAsync(hostId, result, pointsChanged: true, ct);
        if (result is CommunityOperationOutcome.Succeeded { WasIdempotent: false })
        {
            await NotifyAchievementCompletionsAsync(
                hostId,
                $"event:{sourceEvent.Kind}:{sourceEvent.SourceEventId}",
                ct
            );
        }
        return result;
    }

    private async Task<CommunityOperationOutcome> ProcessEventAttemptAsync(
        int hostId,
        CommunitySourceEvent sourceEvent,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (!await FeatureEnabledAsync(db, hostId, ct))
        {
            return new CommunityOperationOutcome.FeatureDisabled();
        }
        var acceptEventsAfterUtc = await db
            .Hosts.Where(value => value.Id == hostId)
            .Select(value => value.CommunityProgressionAcceptEventsAfterUtc)
            .SingleAsync(ct);
        if (acceptEventsAfterUtc is { } cutoff && sourceEvent.OccurredAtUtc.UtcDateTime < cutoff)
        {
            await transaction.CommitAsync(ct);
            return new CommunityOperationOutcome.Succeeded(true);
        }
        var now = clock.GetUtcNow().UtcDateTime;
        var claimed = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT OR IGNORE INTO community_source_event_receipts
                (HostId, SourceKind, SourceEventId, ProcessedAtUtc)
            VALUES
                ({hostId}, {PersistedEnumTokens<CommunityEventRuleKind>.Format(
                sourceEvent.Kind
            )}, {sourceEvent.SourceEventId}, {now});
            """,
            ct
        );
        if (claimed == 0)
        {
            await transaction.CommitAsync(ct);
            return new CommunityOperationOutcome.Succeeded(true);
        }

        var seasons = await db
            .CommunitySeasons.Include(value => value.Definitions)
                .ThenInclude(value => value.Rewards)
            .Where(value =>
                value.HostId == hostId
                && value.Status == CommunitySeasonStatus.Open
                && value.StartsAtUtc <= sourceEvent.OccurredAtUtc.UtcDateTime
                && value.EndsAtUtc >= sourceEvent.OccurredAtUtc.UtcDateTime
            )
            .ToListAsync(ct);
        var changed = false;
        foreach (var season in seasons)
        {
            foreach (
                var definition in season.Definitions.Where(value =>
                    value.EventRule == sourceEvent.Kind
                    && FilterMatches(value.FilterToken, sourceEvent.FilterToken)
                    && (
                        value.Scope == CommunityProgressScope.Communal
                        || sourceEvent.Viewer is not null
                    )
                )
            )
            {
                var period =
                    definition.ResetCadence == CommunityResetCadence.None
                        ? null
                        : await EnsureCurrentPeriodAsync(
                            db,
                            season,
                            definition,
                            CommunityRolloverKind.Restart,
                            $"event:{sourceEvent.Kind}:{sourceEvent.SourceEventId}:{definition.Id}",
                            now,
                            ct
                        );
                var subjectKey =
                    definition.Scope == CommunityProgressScope.Communal
                        ? "community"
                        : $"viewer:{sourceEvent.Viewer!.TwitchUserId}";
                var progress = await db.CommunityProgress.SingleOrDefaultAsync(
                    value =>
                        value.HostId == hostId
                        && value.DefinitionId == definition.Id
                        && value.SubjectKey == subjectKey,
                    ct
                );
                if (progress is null)
                {
                    progress = new CommunityProgress
                    {
                        HostId = hostId,
                        SeasonId = season.Id,
                        DefinitionId = definition.Id,
                        SubjectKey = subjectKey,
                        ViewerTwitchUserId = sourceEvent.Viewer?.TwitchUserId,
                        ViewerLogin = sourceEvent.Viewer is null
                            ? null
                            : CommunityInput.NormalizeLogin(sourceEvent.Viewer.Login),
                        ViewerDisplayName = sourceEvent.Viewer?.DisplayName,
                        PeriodKey = period?.Key,
                        UpdatedAtUtc = now,
                    };
                    _ = db.CommunityProgress.Add(progress);
                }
                if (progress.PeriodKey != period?.Key)
                {
                    progress.Amount = 0;
                    progress.PeriodKey = period?.Key;
                }
                if (
                    definition.CompletionMode == CommunityCompletionMode.OneTime
                    && progress.CompletionCount > 0
                )
                {
                    continue;
                }
                var increment =
                    definition.Increment == CommunityProgressIncrement.EventValue
                        ? sourceEvent.Value
                        : 1;
                progress.Amount = checked(progress.Amount + increment);
                progress.UpdatedAtUtc = now;
                changed = true;
                _ = db.CommunityEvents.Add(
                    DomainEvent(
                        hostId,
                        season.Id,
                        CommunityEventKind.ProgressAdvanced,
                        $"progress:{sourceEvent.Kind}:{sourceEvent.SourceEventId}:{definition.Id}",
                        JsonSerializer.Serialize(
                            new
                            {
                                seasonId = season.PublicId,
                                definitionId = definition.PublicId,
                                amount = progress.Amount,
                                target = definition.Target,
                            }
                        ),
                        now
                    )
                );
                while (
                    progress.Amount >= definition.Target
                    && (
                        definition.CompletionMode == CommunityCompletionMode.Repeatable
                        || progress.CompletionCount == 0
                    )
                )
                {
                    var completion = await CompleteAsync(
                        db,
                        season,
                        definition,
                        progress,
                        sourceEvent.Viewer,
                        $"event:{sourceEvent.Kind}:{sourceEvent.SourceEventId}",
                        now,
                        ct
                    );
                    if (completion is null)
                    {
                        return new CommunityOperationOutcome.Conflict(
                            "A point reward would exceed the viewer balance limit."
                        );
                    }
                    progress.Amount =
                        definition.CompletionMode == CommunityCompletionMode.Repeatable
                            ? progress.Amount - definition.Target
                            : definition.Target;
                }
            }
        }
        _ = await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new CommunityOperationOutcome.Succeeded(!changed);
    }

    public async Task<CommunityOperationOutcome> EditScheduleAsync(
        int hostId,
        CommunityScheduleEditCommand command,
        CancellationToken ct
    )
    {
        if (Validate(command.Schedule) is { } invalid)
        {
            return invalid;
        }
        if (!command.ConfirmActiveProgressReset)
        {
            return new CommunityOperationOutcome.Invalid(
                "Confirm that saving closes the current period and resets active repeatable progress."
            );
        }
        if (!await FeatureEnabledAsync(hostId, ct))
        {
            return new CommunityOperationOutcome.FeatureDisabled();
        }

        var result = await RetryAsync<CommunityOperationOutcome>(
            async () =>
            {
                await using var db = await dbFactory.CreateDbContextAsync(ct);
                await using var transaction = await db.Database.BeginTransactionAsync(ct);
                if (!await FeatureEnabledAsync(db, hostId, ct))
                {
                    return new CommunityOperationOutcome.FeatureDisabled();
                }
                var operationKey = $"schedule-edit:{command.OperationId:N}";
                if (
                    await db.CommunityAudits.AnyAsync(
                        value =>
                            value.HostId == hostId
                            && value.Action == "ScheduleEdited"
                            && value.OperationKey == operationKey,
                        ct
                    )
                )
                {
                    return new CommunityOperationOutcome.Succeeded(true);
                }
                var definition = await db.CommunityDefinitions.SingleOrDefaultAsync(
                    value => value.HostId == hostId && value.PublicId == command.DefinitionId.Value,
                    ct
                );
                if (definition is null)
                {
                    return new CommunityOperationOutcome.NotFound();
                }
                if (definition.CompletionMode != CommunityCompletionMode.Repeatable)
                {
                    return new CommunityOperationOutcome.Conflict(
                        "Only repeatable definitions have reset schedules."
                    );
                }
                var season = await db.CommunitySeasons.SingleAsync(
                    value => value.HostId == hostId && value.Id == definition.SeasonId,
                    ct
                );
                if (season.Status != CommunitySeasonStatus.Open)
                {
                    return new CommunityOperationOutcome.Conflict(
                        "An immediate rollover only applies to an open season."
                    );
                }
                var now = clock.GetUtcNow().UtcDateTime;
                var active = await db
                    .CommunityResetPeriods.Where(value =>
                        value.HostId == hostId
                        && value.DefinitionId == definition.Id
                        && value.ClosedAtUtc == null
                    )
                    .ToListAsync(ct);
                foreach (var period in active)
                {
                    period.ClosedAtUtc = now;
                }
                definition.ResetCadence = command.Schedule.Cadence;
                definition.ResetLocalTime = command.Schedule.LocalTime.ToString(
                    "HH:mm",
                    CultureInfo.InvariantCulture
                );
                definition.ResetWeekday = command.Schedule.Weekday is { } weekday
                    ? (int)weekday
                    : null;
                definition.ScheduleRevision++;
                _ = await db
                    .CommunityProgress.Where(value =>
                        value.HostId == hostId && value.DefinitionId == definition.Id
                    )
                    .ExecuteUpdateAsync(
                        setters =>
                            setters
                                .SetProperty(value => value.Amount, 0)
                                .SetProperty(value => value.PeriodKey, (string?)null)
                                .SetProperty(value => value.UpdatedAtUtc, now),
                        ct
                    );
                if (command.Schedule.Cadence != CommunityResetCadence.None)
                {
                    _ = await EnsureCurrentPeriodAsync(
                        db,
                        season,
                        definition,
                        CommunityRolloverKind.ScheduleEdit,
                        operationKey,
                        now,
                        ct
                    );
                }
                _ = db.CommunityAudits.Add(
                    Audit(
                        hostId,
                        season,
                        definition,
                        "ScheduleEdited",
                        operationKey,
                        Normalize(command.Actor),
                        command.PrivateNote,
                        now
                    )
                );
                _ = await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return new CommunityOperationOutcome.Succeeded();
            },
            ct
        );
        await PublishIfChangedAsync(hostId, result, pointsChanged: false, ct);
        return result;
    }

    public async Task RollOverCurrentPeriodsAsync(CommunityRolloverKind kind, CancellationToken ct)
    {
        var hostIds = await LoadEnabledHostIdsAsync(ct);
        foreach (var hostId in hostIds)
        {
            var changed = await RetryAsync<bool>(
                async () =>
                {
                    await using var db = await dbFactory.CreateDbContextAsync(ct);
                    await using var transaction = await db.Database.BeginTransactionAsync(ct);
                    if (!await FeatureEnabledAsync(db, hostId, ct))
                    {
                        return false;
                    }
                    var seasons = await db
                        .CommunitySeasons.Include(value => value.Definitions)
                        .Where(value =>
                            value.HostId == hostId && value.Status == CommunitySeasonStatus.Open
                        )
                        .ToListAsync(ct);
                    var now = clock.GetUtcNow().UtcDateTime;
                    var changed = false;
                    foreach (var season in seasons)
                    {
                        foreach (
                            var definition in season.Definitions.Where(value =>
                                value.ResetCadence != CommunityResetCadence.None
                            )
                        )
                        {
                            changed |=
                                await EnsureCurrentPeriodAsync(
                                    db,
                                    season,
                                    definition,
                                    kind,
                                    $"{kind}:{definition.Id}:{now:yyyyMMddHHmm}",
                                    now,
                                    ct
                                )
                                    is not null;
                        }
                    }
                    _ = await db.SaveChangesAsync(ct);
                    await transaction.CommitAsync(ct);
                    return changed;
                },
                ct
            );
            if (changed)
            {
                _ = await events.PublishAsync(AppEventKind.CommunityProgressionChanged, ct);
                foreach (var observer in _changeObservers)
                {
                    await observer.CommunityProgressionChangedAsync(hostId, ct);
                }
            }
        }
    }

    public async Task ReconcileCompletedBountyEventsAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var hosts = await db
            .Hosts.AsNoTracking()
            .Where(value => (value.EnabledFeatures & _requiredFeature) == _requiredFeature)
            .Select(value => new { value.Id, value.CommunityProgressionAcceptEventsAfterUtc })
            .ToListAsync(ct);
        var candidates = new List<(int HostId, Guid PublicId, DateTime CompletedAtUtc)>();
        foreach (var host in hosts)
        {
            var completed = await db
                .Bounties.AsNoTracking()
                .Where(value =>
                    value.HostId == host.Id
                    && value.Status == BountyStatus.Completed
                    && value.ResolvedAtUtc != null
                    && (
                        host.CommunityProgressionAcceptEventsAfterUtc == null
                        || value.ResolvedAtUtc >= host.CommunityProgressionAcceptEventsAfterUtc
                    )
                )
                .Select(value => new
                {
                    value.PublicId,
                    CompletedAtUtc = value.ResolvedAtUtc!.Value,
                })
                .ToListAsync(ct);
            var receipts = await db
                .CommunitySourceEventReceipts.AsNoTracking()
                .Where(value =>
                    value.HostId == host.Id
                    && value.SourceKind == CommunityEventRuleKind.BountyCompleted
                )
                .Select(value => value.SourceEventId)
                .ToHashSetAsync(StringComparer.Ordinal, ct);
            candidates.AddRange(
                completed
                    .Where(value => !receipts.Contains(value.PublicId.ToString("N")))
                    .Select(value => (host.Id, value.PublicId, value.CompletedAtUtc))
            );
        }
        await db.DisposeAsync();

        foreach (var candidate in candidates)
        {
            _ = await ProcessEventAsync(
                candidate.HostId,
                new CommunitySourceEvent.BountyCompleted(
                    candidate.PublicId.ToString("N"),
                    new DateTimeOffset(candidate.CompletedAtUtc, TimeSpan.Zero)
                ),
                ct
            );
        }
    }

    private static async Task<bool> HasUnreconciledBountyEventsAsync(
        BlokeBotDbContext db,
        CommunitySeason season,
        CancellationToken ct
    )
    {
        if (
            !season.Definitions.Any(value =>
                value.EventRule == CommunityEventRuleKind.BountyCompleted
            )
        )
        {
            return false;
        }
        var acceptEventsAfterUtc = await db
            .Hosts.Where(value => value.Id == season.HostId)
            .Select(value => value.CommunityProgressionAcceptEventsAfterUtc)
            .SingleAsync(ct);
        var completed = await db
            .Bounties.AsNoTracking()
            .Where(value =>
                value.HostId == season.HostId
                && value.Status == BountyStatus.Completed
                && value.ResolvedAtUtc >= season.StartsAtUtc
                && value.ResolvedAtUtc <= season.EndsAtUtc
                && (acceptEventsAfterUtc == null || value.ResolvedAtUtc >= acceptEventsAfterUtc)
            )
            .Select(value => value.PublicId)
            .ToListAsync(ct);
        if (completed.Count == 0)
        {
            return false;
        }
        var receipts = await db
            .CommunitySourceEventReceipts.AsNoTracking()
            .Where(value =>
                value.HostId == season.HostId
                && value.SourceKind == CommunityEventRuleKind.BountyCompleted
            )
            .Select(value => value.SourceEventId)
            .ToHashSetAsync(StringComparer.Ordinal, ct);
        return completed.Any(value => !receipts.Contains(value.ToString("N")));
    }

    public async Task<CommunityOperationOutcome> EquipAsync(
        CommunityEquipCommand command,
        CancellationToken ct
    )
    {
        if (!await FeatureEnabledAsync(command.HostId, ct))
        {
            return new CommunityOperationOutcome.FeatureDisabled();
        }
        if (Validate(command.Viewer) is { } invalid)
        {
            return invalid;
        }
        var result = await RetryAsync<CommunityOperationOutcome>(
            async () =>
            {
                await using var db = await dbFactory.CreateDbContextAsync(ct);
                await using var transaction = await db.Database.BeginTransactionAsync(ct);
                if (!await FeatureEnabledAsync(db, command.HostId, ct))
                {
                    return new CommunityOperationOutcome.FeatureDisabled();
                }
                var viewer = Normalize(command.Viewer);
                var rewardKey = NormalizeKey(command.RewardKey);
                var operationKey =
                    $"equip:{viewer.TwitchUserId}:{command.Kind}:{command.OperationId:N}";
                var priorOperation = await db.CommunityEvents.SingleOrDefaultAsync(
                    value =>
                        value.HostId == command.HostId
                        && value.Kind == CommunityEventKind.RewardEquipped
                        && value.OperationKey == operationKey,
                    ct
                );
                if (priorOperation is not null)
                {
                    await transaction.CommitAsync(ct);
                    return EquippedRewardKey(priorOperation.PublicPayload) == rewardKey
                        ? new CommunityOperationOutcome.Succeeded(true)
                        : new CommunityOperationOutcome.Conflict(
                            "That equip operation ID was already used for another reward."
                        );
                }
                var reward = await db.CommunityRewardDefinitions.SingleOrDefaultAsync(
                    value =>
                        value.HostId == command.HostId
                        && value.Kind == command.Kind
                        && value.Key == rewardKey,
                    ct
                );
                if (reward is null)
                {
                    return new CommunityOperationOutcome.NotFound();
                }
                if (
                    !await db.CommunityRewardUnlocks.AnyAsync(
                        value =>
                            value.HostId == command.HostId
                            && value.RewardDefinitionId == reward.Id
                            && value.ViewerTwitchUserId == viewer.TwitchUserId,
                        ct
                    )
                )
                {
                    return new CommunityOperationOutcome.Conflict(
                        "That reward is not unlocked for this viewer and host."
                    );
                }
                var equipped = await db.CommunityEquippedRewards.SingleOrDefaultAsync(
                    value =>
                        value.HostId == command.HostId
                        && value.ViewerTwitchUserId == viewer.TwitchUserId
                        && value.Kind == command.Kind,
                    ct
                );
                var now = clock.GetUtcNow().UtcDateTime;
                if (equipped is null)
                {
                    equipped = new CommunityEquippedReward
                    {
                        HostId = command.HostId,
                        ViewerTwitchUserId = viewer.TwitchUserId,
                        Kind = command.Kind,
                    };
                    _ = db.CommunityEquippedRewards.Add(equipped);
                }
                equipped.RewardDefinitionId = reward.Id;
                equipped.ViewerLogin = viewer.Login;
                equipped.LastOperationId = command.OperationId;
                equipped.EquippedAtUtc = now;
                _ = db.CommunityEvents.Add(
                    DomainEvent(
                        command.HostId,
                        reward.SeasonId,
                        CommunityEventKind.RewardEquipped,
                        operationKey,
                        JsonSerializer.Serialize(
                            new
                            {
                                viewer = viewer.DisplayName,
                                reward = reward.Name,
                                rewardKey,
                                reward.Kind,
                            }
                        ),
                        now
                    )
                );
                _ = await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return new CommunityOperationOutcome.Succeeded();
            },
            ct
        );
        await PublishIfChangedAsync(command.HostId, result, pointsChanged: false, ct);
        return result;
    }

    public async Task<CommunityExternalGrantOutcome> GrantAsync(
        CommunityExternalGrantRequest request,
        CancellationToken cancellationToken
    )
    {
        if (Validate(request) is { } invalid)
        {
            return new CommunityExternalGrantOutcome.Invalid(invalid.Message);
        }
        if (!await FeatureEnabledAsync(request.HostId, cancellationToken))
        {
            return new CommunityExternalGrantOutcome.FeatureDisabled();
        }
        var result = await RetryAsync<CommunityExternalGrantOutcome>(
            () => GrantAttemptAsync(request, cancellationToken),
            cancellationToken
        );
        if (result is CommunityExternalGrantOutcome.Granted { WasIdempotent: false } granted)
        {
            _ = await events.PublishAsync(
                AppEventKind.CommunityProgressionChanged,
                cancellationToken
            );
            foreach (var observer in _changeObservers)
            {
                await observer.CommunityProgressionChangedAsync(request.HostId, cancellationToken);
            }
            _ = await events.PublishAsync(AppEventKind.PointsChanged, cancellationToken);
            await NotifyAchievementCompletionAsync(
                request.HostId,
                granted.CompletionId,
                cancellationToken
            );
        }
        return result;
    }

    private async Task<CommunityExternalGrantOutcome> GrantAttemptAsync(
        CommunityExternalGrantRequest request,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (!await FeatureEnabledAsync(db, request.HostId, ct))
        {
            return new CommunityExternalGrantOutcome.FeatureDisabled();
        }
        var acceptEventsAfterUtc = await db
            .Hosts.Where(value => value.Id == request.HostId)
            .Select(value => value.CommunityProgressionAcceptEventsAfterUtc)
            .SingleAsync(ct);
        if (acceptEventsAfterUtc is { } cutoff && request.OccurredAtUtc.UtcDateTime < cutoff)
        {
            return new CommunityExternalGrantOutcome.AchievementUnavailable();
        }
        var fingerprint = Fingerprint(
            $"{request.AchievementKey.Value}\n{request.Viewer.TwitchUserId}"
        );
        var existing = await db.CommunityExternalGrantReceipts.SingleOrDefaultAsync(
            value =>
                value.HostId == request.HostId
                && value.Source == request.Source
                && value.IdempotencyKey == request.IdempotencyKey,
            ct
        );
        if (existing is not null)
        {
            if (!string.Equals(existing.Fingerprint, fingerprint, StringComparison.Ordinal))
            {
                return new CommunityExternalGrantOutcome.Conflict();
            }
            var completionId = await db
                .CommunityCompletions.Where(value => value.Id == existing.CompletionId!.Value)
                .Select(value => value.PublicId)
                .SingleAsync(ct);
            await transaction.CommitAsync(ct);
            return new CommunityExternalGrantOutcome.Granted(completionId, true);
        }
        var definition = await db
            .CommunityDefinitions.Include(value => value.Rewards)
            .SingleOrDefaultAsync(
                value =>
                    value.HostId == request.HostId
                    && value.Key == request.AchievementKey.Value
                    && value.Kind == CommunityDefinitionKind.Achievement,
                ct
            );
        if (definition is null)
        {
            return new CommunityExternalGrantOutcome.AchievementNotFound();
        }
        if (
            definition.EventRule != CommunityEventRuleKind.ExternalGrant
            || definition.Scope != CommunityProgressScope.Viewer
        )
        {
            return new CommunityExternalGrantOutcome.AchievementUnavailable();
        }
        var season = await db.CommunitySeasons.SingleAsync(
            value => value.HostId == request.HostId && value.Id == definition.SeasonId,
            ct
        );
        if (season.Status != CommunitySeasonStatus.Open)
        {
            return new CommunityExternalGrantOutcome.AchievementUnavailable();
        }
        if (
            request.OccurredAtUtc.UtcDateTime < season.StartsAtUtc
            || request.OccurredAtUtc.UtcDateTime > season.EndsAtUtc
        )
        {
            return new CommunityExternalGrantOutcome.AchievementUnavailable();
        }
        var viewer = Normalize(request.Viewer);
        var subjectKey = $"viewer:{viewer.TwitchUserId}";
        var progress = await db.CommunityProgress.SingleOrDefaultAsync(
            value =>
                value.HostId == request.HostId
                && value.DefinitionId == definition.Id
                && value.SubjectKey == subjectKey,
            ct
        );
        if (progress is null)
        {
            progress = new CommunityProgress
            {
                HostId = request.HostId,
                SeasonId = season.Id,
                DefinitionId = definition.Id,
                SubjectKey = subjectKey,
                ViewerTwitchUserId = viewer.TwitchUserId,
                ViewerLogin = viewer.Login,
                ViewerDisplayName = viewer.DisplayName,
                UpdatedAtUtc = request.OccurredAtUtc.UtcDateTime,
            };
            _ = db.CommunityProgress.Add(progress);
        }
        if (
            definition.CompletionMode == CommunityCompletionMode.OneTime
            && progress.CompletionCount > 0
        )
        {
            return new CommunityExternalGrantOutcome.AchievementUnavailable();
        }
        progress.Amount = definition.Target;
        var operationKey = $"external:{request.Source}:{request.IdempotencyKey}";
        var completion = await CompleteAsync(
            db,
            season,
            definition,
            progress,
            viewer,
            operationKey,
            request.OccurredAtUtc.UtcDateTime,
            ct
        );
        if (completion is null)
        {
            return new CommunityExternalGrantOutcome.Conflict();
        }
        _ = db.CommunityExternalGrantReceipts.Add(
            new()
            {
                HostId = request.HostId,
                Source = request.Source,
                IdempotencyKey = request.IdempotencyKey,
                Fingerprint = fingerprint,
                CompletionId = completion.Id,
                ProcessedAtUtc = request.OccurredAtUtc.UtcDateTime,
            }
        );
        _ = await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new CommunityExternalGrantOutcome.Granted(completion.PublicId, false);
    }

    public async Task<IReadOnlyList<CommunitySeasonView>> GetModeratorSeasonsAsync(
        int hostId,
        CancellationToken ct
    )
    {
        if (!await FeatureEnabledAsync(hostId, ct))
        {
            return [];
        }
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var host = await db.Hosts.AsNoTracking().SingleAsync(value => value.Id == hostId, ct);
        var seasons = await db
            .CommunitySeasons.AsNoTracking()
            .AsSplitQuery()
            .Include(value => value.Definitions)
            .Include(value => value.Rewards)
            .Where(value => value.HostId == hostId)
            .OrderByDescending(value => value.CreatedAtUtc)
            .ToListAsync(ct);
        var result = new List<CommunitySeasonView>();
        foreach (var season in seasons)
        {
            result.Add(
                ToSeasonView(
                    season,
                    host.TimeZoneId,
                    clock.GetUtcNow(),
                    await LoadSeasonActivityAsync(db, season, ct)
                )
            );
        }
        return result;
    }

    public async Task<CommunityPublicView?> GetPublicAsync(string hostLogin, CancellationToken ct)
    {
        var normalized = CommunityInput.NormalizeLogin(hostLogin);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var host = await db
            .Hosts.AsNoTracking()
            .SingleOrDefaultAsync(value => value.Login == normalized, ct);
        if (host is null || !host.EnabledFeatures.Contains(_requiredFeature))
        {
            return null;
        }
        var seasons = await db
            .CommunitySeasons.AsNoTracking()
            .Where(value =>
                value.HostId == host.Id
                && value.Visibility == CommunityVisibility.Public
                && value.Status != CommunitySeasonStatus.Draft
            )
            .OrderByDescending(value => value.StartsAtUtc)
            .ToListAsync(ct);
        if (seasons.Count == 0)
        {
            return null;
        }
        var passportExclusions = host.EnabledFeatures.Contains(HostFeatureFlags.ViewerPassports)
            ? await db
                .ViewerPassports.AsNoTracking()
                .Where(value =>
                    value.HostId == host.Id && value.Visibility != ViewerPassportVisibility.Public
                )
                .Select(value => new { value.TwitchUserId, value.Login })
                .ToArrayAsync(ct)
            : [];
        var excludedIds = passportExclusions
            .Select(value => value.TwitchUserId)
            .ToHashSet(StringComparer.Ordinal);
        var excludedLogins = passportExclusions
            .Where(value => !string.IsNullOrWhiteSpace(value.Login))
            .Select(value => value.Login)
            .ToHashSet(StringComparer.Ordinal);
        var result = new List<CommunityPublicSeasonView>();
        foreach (var season in seasons)
        {
            result.Add(await ToPublicSeasonAsync(db, season, excludedIds, excludedLogins, ct));
        }
        return new(host.Login, result);
    }

    public async Task<IReadOnlyList<CommunityUnlockView>> GetViewerUnlocksAsync(
        int hostId,
        string viewerTwitchUserId,
        CancellationToken ct
    )
    {
        if (!await FeatureEnabledAsync(hostId, ct))
        {
            return [];
        }
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await LoadUnlocksAsync(db, hostId, viewerTwitchUserId, null, ct);
    }

    private async Task<CommunityPublicSeasonView> ToPublicSeasonAsync(
        BlokeBotDbContext db,
        CommunitySeason season,
        IReadOnlySet<string> excludedTwitchUserIds,
        IReadOnlySet<string> excludedLogins,
        CancellationToken ct
    )
    {
        var activity = await LoadSeasonActivityAsync(db, season, ct);
        var definitions = await db
            .CommunityDefinitions.AsNoTracking()
            .Where(value => value.HostId == season.HostId && value.SeasonId == season.Id)
            .OrderBy(value => value.CreatedAtUtc)
            .ThenBy(value => value.Id)
            .Select(value => new CommunityPublicDefinitionView(
                new(value.PublicId),
                value.Name,
                value.Kind,
                value.Target
            ))
            .ToArrayAsync(ct);
        return new(
            new(season.PublicId),
            season.Name,
            season.Description,
            season.Status,
            season.StartsAtUtc,
            season.EndsAtUtc,
            definitions,
            activity
                .Standings.Where(value =>
                    Visible(value.TwitchUserId, value.Login, excludedTwitchUserIds, excludedLogins)
                )
                .ToArray(),
            activity
                .Progress.Where(value =>
                    Visible(value.TwitchUserId, value.Login, excludedTwitchUserIds, excludedLogins)
                )
                .ToArray(),
            activity.CommunalProgress,
            activity
                .Completions.Where(value =>
                    value.TwitchUserId is null
                    || Visible(
                        value.TwitchUserId,
                        value.Login,
                        excludedTwitchUserIds,
                        excludedLogins
                    )
                )
                .ToArray(),
            activity
                .Unlocks.Where(value =>
                    Visible(value.TwitchUserId, value.Login, excludedTwitchUserIds, excludedLogins)
                )
                .ToArray()
        );
    }

    private static bool Visible(
        string twitchUserId,
        string? login,
        IReadOnlySet<string> excludedTwitchUserIds,
        IReadOnlySet<string> excludedLogins
    ) =>
        !excludedTwitchUserIds.Contains(twitchUserId)
        && (login is null || !excludedLogins.Contains(login));

    private async Task<CommunitySeasonActivity> LoadSeasonActivityAsync(
        BlokeBotDbContext db,
        CommunitySeason season,
        CancellationToken ct
    )
    {
        var definitions = await db
            .CommunityDefinitions.AsNoTracking()
            .Where(value => value.HostId == season.HostId && value.SeasonId == season.Id)
            .ToDictionaryAsync(value => value.Id, ct);
        var standings = season.Status
            is CommunitySeasonStatus.Closed
                or CommunitySeasonStatus.Archived
            ? await db
                .CommunitySeasonStandings.AsNoTracking()
                .Where(value => value.HostId == season.HostId && value.SeasonId == season.Id)
                .OrderBy(value => value.Rank)
                .Select(value => new CommunityStandingView(
                    value.Rank,
                    value.ViewerTwitchUserId,
                    value.ViewerLogin,
                    value.ViewerDisplayName,
                    value.CompletedCount,
                    value.ProgressAmount
                ))
                .ToListAsync(ct)
            : await LiveStandingsAsync(db, season, ct);
        var progress = await db
            .CommunityProgress.AsNoTracking()
            .Where(value => value.HostId == season.HostId && value.SeasonId == season.Id)
            .ToListAsync(ct);
        var completions = await db
            .CommunityCompletions.AsNoTracking()
            .Where(value => value.HostId == season.HostId && value.SeasonId == season.Id)
            .OrderByDescending(value => value.CompletedAtUtc)
            .ToListAsync(ct);
        var unlocks = await LoadUnlocksAsync(db, season.HostId, null, season.Id, ct);
        return new CommunitySeasonActivity(
            standings,
            progress
                .Where(value => value.ViewerTwitchUserId is not null)
                .Select(value =>
                {
                    var definition = definitions[value.DefinitionId];
                    return new CommunityViewerProgressView(
                        value.ViewerTwitchUserId!,
                        value.ViewerLogin!,
                        value.ViewerDisplayName!,
                        definition.Name,
                        definition.Kind,
                        value.Amount,
                        definition.Target,
                        value.CompletionCount,
                        value.PeriodKey
                    );
                })
                .ToArray(),
            progress
                .Where(value => value.ViewerTwitchUserId is null)
                .Select(value =>
                {
                    var definition = definitions[value.DefinitionId];
                    return new CommunityCommunalProgressView(
                        definition.Name,
                        definition.Kind,
                        value.Amount,
                        definition.Target,
                        value.CompletionCount,
                        value.PeriodKey
                    );
                })
                .ToArray(),
            completions
                .Select(value =>
                {
                    var definition = definitions[value.DefinitionId];
                    return new CommunityCompletionView(
                        value.PublicId,
                        value.ViewerTwitchUserId,
                        value.ViewerLogin,
                        value.ViewerDisplayName,
                        value.DefinitionName,
                        definition.Kind,
                        value.CompletedAtUtc,
                        value.RewardSnapshot
                    );
                })
                .ToArray(),
            unlocks
        );
    }

    private static async Task<IReadOnlyList<CommunityStandingView>> LiveStandingsAsync(
        BlokeBotDbContext db,
        CommunitySeason season,
        CancellationToken ct
    )
    {
        var progress = await db
            .CommunityProgress.AsNoTracking()
            .Where(value =>
                value.HostId == season.HostId
                && value.SeasonId == season.Id
                && value.ViewerTwitchUserId != null
            )
            .ToListAsync(ct);
        return progress
            .GroupBy(value => value.ViewerTwitchUserId!)
            .Select(group => new
            {
                TwitchUserId = group.Key,
                Login = group.OrderByDescending(value => value.UpdatedAtUtc).First().ViewerLogin!,
                DisplayName = group
                    .OrderByDescending(value => value.UpdatedAtUtc)
                    .First()
                    .ViewerDisplayName!,
                Completed = group.Sum(value => value.CompletionCount),
                Progress = group.Sum(value => value.Amount),
            })
            .OrderByDescending(value => value.Completed)
            .ThenByDescending(value => value.Progress)
            .ThenBy(value => value.TwitchUserId, StringComparer.Ordinal)
            .Select(
                (value, index) =>
                    new CommunityStandingView(
                        index + 1,
                        value.TwitchUserId,
                        value.Login,
                        value.DisplayName,
                        value.Completed,
                        value.Progress
                    )
            )
            .ToArray();
    }

    private static async Task<IReadOnlyList<CommunityUnlockView>> LoadUnlocksAsync(
        BlokeBotDbContext db,
        int hostId,
        string? viewerTwitchUserId,
        long? seasonId,
        CancellationToken ct
    )
    {
        var query =
            from unlock in db.CommunityRewardUnlocks.AsNoTracking()
            join reward in db.CommunityRewardDefinitions.AsNoTracking()
                on unlock.RewardDefinitionId equals reward.Id
            where unlock.HostId == hostId
            select new { unlock, reward };
        if (viewerTwitchUserId is not null)
        {
            query = query.Where(value => value.unlock.ViewerTwitchUserId == viewerTwitchUserId);
        }
        if (seasonId is not null)
        {
            query = query.Where(value => value.reward.SeasonId == seasonId.Value);
        }
        var rows = await query.ToListAsync(ct);
        var equipped = await db
            .CommunityEquippedRewards.AsNoTracking()
            .Where(value => value.HostId == hostId)
            .Select(value => new { value.ViewerTwitchUserId, value.RewardDefinitionId })
            .ToListAsync(ct);
        var selected = equipped
            .Select(value => (value.ViewerTwitchUserId, value.RewardDefinitionId))
            .ToHashSet();
        return rows.Select(value => new CommunityUnlockView(
                value.unlock.ViewerTwitchUserId,
                value.unlock.ViewerLogin,
                value.reward.Kind,
                value.reward.Name,
                value.reward.PresentationToken,
                value.unlock.GrantedAtUtc,
                selected.Contains((value.unlock.ViewerTwitchUserId, value.reward.Id))
            ))
            .OrderByDescending(value => value.GrantedAtUtc)
            .ToArray();
    }

    private async Task SnapshotStandingsAsync(
        BlokeBotDbContext db,
        CommunitySeason season,
        DateTime now,
        CancellationToken ct
    )
    {
        var standings = await LiveStandingsAsync(db, season, ct);
        foreach (var standing in standings)
        {
            _ = db.CommunitySeasonStandings.Add(
                new()
                {
                    HostId = season.HostId,
                    SeasonId = season.Id,
                    ViewerTwitchUserId = standing.TwitchUserId,
                    ViewerLogin = standing.Login,
                    ViewerDisplayName = standing.DisplayName,
                    CompletedCount = standing.CompletedCount,
                    ProgressAmount = standing.ProgressAmount,
                    Rank = standing.Rank,
                    SnapshottedAtUtc = now,
                }
            );
        }
    }

    private async Task<CommunityCompletion?> CompleteAsync(
        BlokeBotDbContext db,
        CommunitySeason season,
        CommunityDefinition definition,
        CommunityProgress progress,
        CommunityViewer? sourceViewer,
        string operationKey,
        DateTime now,
        CancellationToken ct
    )
    {
        var viewer = sourceViewer is null ? null : Normalize(sourceViewer);
        var rewards = await (
            from link in db.CommunityDefinitionRewards
            join reward in db.CommunityRewardDefinitions on link.RewardDefinitionId equals reward.Id
            where link.DefinitionId == definition.Id
            select reward
        ).ToListAsync(ct);
        var points = PointAmount.ParseAbsolute(definition.PointsReward);
        PointLedgerEntry? pointLedger = null;
        if (viewer is not null && !points.IsZero)
        {
            var balance = await db.PointBalances.SingleOrDefaultAsync(
                value => value.HostId == season.HostId && value.Login == viewer.Login,
                ct
            );
            var current = PointAmount.ParseAbsolute(balance?.Amount ?? "0");
            if (
                !await PointCreditCapacity.CanCreditAsync(
                    db,
                    season.HostId,
                    viewer.Login,
                    current,
                    points.Value,
                    ct
                )
            )
            {
                return null;
            }
            balance ??= new PointBalance { HostId = season.HostId, Login = viewer.Login };
            if (balance.Id == 0)
            {
                _ = db.PointBalances.Add(balance);
            }
            var next = current.Add(points);
            balance.Amount = next.ToString();
            balance.UpdatedAtUtc = now;
            pointLedger = new()
            {
                HostId = season.HostId,
                CreatedAtUtc = now,
                Kind = PointLedgerKind.CommunityProgressionReward,
                Login = viewer.Login,
                Delta = points.ToString(),
                BalanceAfter = next.ToString(),
                Note = $"Community progression: {definition.Name}",
                OperationKey =
                    $"community:{operationKey}:{definition.Id}:{progress.CompletionCount + 1}",
            };
            _ = db.PointLedgerEntries.Add(pointLedger);
        }
        progress.CompletionCount++;
        progress.ViewerTwitchUserId ??= viewer?.TwitchUserId;
        progress.ViewerLogin ??= viewer?.Login;
        progress.ViewerDisplayName ??= viewer?.DisplayName;
        var completion = new CommunityCompletion
        {
            PublicId = Guid.NewGuid(),
            HostId = season.HostId,
            SeasonId = season.Id,
            DefinitionId = definition.Id,
            SubjectKey = progress.SubjectKey,
            ViewerTwitchUserId = viewer?.TwitchUserId,
            ViewerLogin = viewer?.Login,
            ViewerDisplayName = viewer?.DisplayName,
            DefinitionKey = definition.Key,
            DefinitionName = definition.Name,
            Sequence = progress.CompletionCount,
            PeriodKey = progress.PeriodKey,
            PointsGranted = points.ToString(),
            RewardSnapshot = JsonSerializer.Serialize(
                rewards.Select(value => new
                {
                    value.Key,
                    value.Kind,
                    value.Name,
                    value.PresentationToken,
                })
            ),
            SourceOperationKey = operationKey,
            CompletedAtUtc = now,
        };
        _ = db.CommunityCompletions.Add(completion);
        _ = await db.SaveChangesAsync(ct);
        if (pointLedger is { } awardedPoints)
        {
            awardedPoints.CommunityCompletionId = completion.Id;
        }
        if (viewer is not null)
        {
            foreach (var reward in rewards)
            {
                var exists = await db.CommunityRewardUnlocks.AnyAsync(
                    value =>
                        value.HostId == season.HostId
                        && value.RewardDefinitionId == reward.Id
                        && value.ViewerTwitchUserId == viewer.TwitchUserId,
                    ct
                );
                if (exists)
                {
                    continue;
                }
                _ = db.CommunityRewardUnlocks.Add(
                    new()
                    {
                        HostId = season.HostId,
                        RewardDefinitionId = reward.Id,
                        ViewerTwitchUserId = viewer.TwitchUserId,
                        ViewerLogin = viewer.Login,
                        ViewerDisplayName = viewer.DisplayName,
                        CompletionId = completion.Id,
                        GrantedAtUtc = now,
                    }
                );
                _ = db.CommunityEvents.Add(
                    DomainEvent(
                        season.HostId,
                        season.Id,
                        CommunityEventKind.RewardGranted,
                        $"reward:{completion.PublicId:N}:{reward.Id}",
                        JsonSerializer.Serialize(
                            new
                            {
                                viewer = viewer.DisplayName,
                                reward = reward.Name,
                                reward.Kind,
                            }
                        ),
                        now
                    )
                );
            }
        }
        _ = db.CommunityEvents.Add(
            DomainEvent(
                season.HostId,
                season.Id,
                CommunityEventKind.Completed,
                $"completion:{completion.PublicId:N}",
                JsonSerializer.Serialize(
                    new
                    {
                        seasonId = season.PublicId,
                        definitionId = definition.PublicId,
                        viewer = viewer?.DisplayName,
                    }
                ),
                now
            )
        );
        return completion;
    }

    private async Task<CommunityPeriodIdentity?> EnsureCurrentPeriodAsync(
        BlokeBotDbContext db,
        CommunitySeason season,
        CommunityDefinition definition,
        CommunityRolloverKind kind,
        string operationKey,
        DateTime now,
        CancellationToken ct
    )
    {
        if (definition.ResetCadence == CommunityResetCadence.None)
        {
            return null;
        }
        var hostTimeZone = await db
            .Hosts.Where(value => value.Id == season.HostId)
            .Select(value => value.TimeZoneId)
            .SingleAsync(ct);
        var schedule = ToSchedule(definition);
        var period = CommunityResetScheduleResolver.Resolve(
            hostTimeZone,
            schedule,
            definition.ScheduleRevision,
            new DateTimeOffset(EnsureUtc(now))
        );
        var existing = await db.CommunityResetPeriods.SingleOrDefaultAsync(
            value =>
                value.HostId == season.HostId
                && value.DefinitionId == definition.Id
                && value.PeriodKey == period.Key,
            ct
        );
        if (existing is not null)
        {
            return period;
        }
        var active = await db
            .CommunityResetPeriods.Where(value =>
                value.HostId == season.HostId
                && value.DefinitionId == definition.Id
                && value.ClosedAtUtc == null
            )
            .ToListAsync(ct);
        foreach (var row in active)
        {
            row.ClosedAtUtc = now;
        }
        _ = db.CommunityResetPeriods.Add(
            new()
            {
                HostId = season.HostId,
                DefinitionId = definition.Id,
                PeriodKey = period.Key,
                RolloverKind = kind,
                OperationKey = operationKey,
                StartedAtUtc = period.StartedAtUtc.UtcDateTime,
                CreatedAtUtc = now,
            }
        );
        _ = await db
            .CommunityProgress.Where(value =>
                value.HostId == season.HostId && value.DefinitionId == definition.Id
            )
            .ExecuteUpdateAsync(
                setters =>
                    setters
                        .SetProperty(value => value.Amount, 0)
                        .SetProperty(value => value.PeriodKey, period.Key)
                        .SetProperty(value => value.UpdatedAtUtc, now),
                ct
            );
        _ = db.CommunityAudits.Add(
            Audit(
                season.HostId,
                season,
                definition,
                "PeriodRolledOver",
                operationKey,
                new(string.Empty, "system"),
                $"{kind}: {period.Key}",
                now
            )
        );
        _ = db.CommunityEvents.Add(
            DomainEvent(
                season.HostId,
                season.Id,
                CommunityEventKind.PeriodRolledOver,
                $"period:{definition.Id}:{period.Key}",
                JsonSerializer.Serialize(
                    new
                    {
                        definitionId = definition.PublicId,
                        period = period.Key,
                        nextResetUtc = period.NextResetUtc,
                    }
                ),
                now
            )
        );
        return period;
    }

    private static CommunitySeasonView ToSeasonView(
        CommunitySeason season,
        string timeZoneId,
        DateTimeOffset now,
        CommunitySeasonActivity activity
    ) =>
        new(
            new(season.PublicId),
            season.Name,
            season.Description,
            season.Status,
            season.Visibility,
            season.StartsAtUtc,
            season.EndsAtUtc,
            season.Revision,
            season
                .Definitions.Select(value =>
                {
                    var schedule = ToSchedule(value);
                    var next =
                        schedule.Cadence == CommunityResetCadence.None
                            ? (DateTimeOffset?)null
                            : CommunityResetScheduleResolver
                                .Resolve(timeZoneId, schedule, value.ScheduleRevision, now)
                                .NextResetUtc;
                    return new CommunityDefinitionView(
                        new(value.PublicId),
                        value.Key,
                        value.Name,
                        value.Kind,
                        value.Scope,
                        value.CompletionMode,
                        value.EventRule,
                        value.Increment,
                        value.Target,
                        PointAmount.ParseAbsolute(value.PointsReward),
                        schedule,
                        timeZoneId,
                        next
                    );
                })
                .ToArray(),
            season
                .Rewards.Select(value => new CommunityRewardView(
                    new(value.PublicId),
                    value.Key,
                    value.Kind,
                    value.Name,
                    value.PresentationToken
                ))
                .ToArray(),
            activity.Standings,
            activity.Progress,
            activity.CommunalProgress,
            activity.Completions,
            activity.Unlocks
        );

    private sealed record CommunitySeasonActivity(
        IReadOnlyList<CommunityStandingView> Standings,
        IReadOnlyList<CommunityViewerProgressView> Progress,
        IReadOnlyList<CommunityCommunalProgressView> CommunalProgress,
        IReadOnlyList<CommunityCompletionView> Completions,
        IReadOnlyList<CommunityUnlockView> Unlocks
    );

    private static CommunityResetSchedule ToSchedule(CommunityDefinition definition) =>
        new(
            definition.ResetCadence,
            TimeOnly.ParseExact(definition.ResetLocalTime, "HH:mm", CultureInfo.InvariantCulture),
            definition.ResetWeekday is { } weekday ? (DayOfWeek)weekday : null
        );

    private async Task<IReadOnlyList<int>> LoadEnabledHostIdsAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db
            .Hosts.AsNoTracking()
            .Where(value => (value.EnabledFeatures & _requiredFeature) == _requiredFeature)
            .Select(value => value.Id)
            .ToListAsync(ct);
    }

    private Task<bool> FeatureEnabledAsync(int hostId, CancellationToken ct) =>
        HostFeatureAvailability.IsEnabledAsync(dbFactory, hostId, _requiredFeature, ct);

    private static Task<bool> FeatureEnabledAsync(
        BlokeBotDbContext db,
        int hostId,
        CancellationToken ct
    ) =>
        db.Hosts.AnyAsync(
            value =>
                value.Id == hostId
                && (value.EnabledFeatures & _requiredFeature) == _requiredFeature,
            ct
        );

    private async Task PublishIfChangedAsync(
        int hostId,
        CommunityOperationOutcome result,
        bool pointsChanged,
        CancellationToken ct
    )
    {
        if (result is not CommunityOperationOutcome.Succeeded { WasIdempotent: false })
        {
            return;
        }
        _ = await events.PublishAsync(AppEventKind.CommunityProgressionChanged, ct);
        foreach (var observer in _changeObservers)
        {
            await observer.CommunityProgressionChangedAsync(hostId, ct);
        }
        if (pointsChanged)
        {
            _ = await events.PublishAsync(AppEventKind.PointsChanged, ct);
        }
    }

    private async Task NotifyAchievementCompletionsAsync(
        int hostId,
        string operationKey,
        CancellationToken ct
    )
    {
        if (_achievementObservers.Length == 0)
        {
            return;
        }
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var completionIds = await (
            from completion in db.CommunityCompletions.AsNoTracking()
            join definition in db.CommunityDefinitions.AsNoTracking()
                on completion.DefinitionId equals definition.Id
            where
                completion.HostId == hostId
                && completion.SourceOperationKey == operationKey
                && definition.Kind == CommunityDefinitionKind.Achievement
            orderby completion.Id
            select completion.PublicId
        ).ToArrayAsync(ct);
        foreach (var completionId in completionIds)
        {
            await NotifyAchievementCompletionAsync(hostId, completionId, ct);
        }
    }

    private async Task NotifyAchievementCompletionAsync(
        int hostId,
        Guid completionId,
        CancellationToken ct
    )
    {
        foreach (var observer in _achievementObservers)
        {
            await observer.AchievementCompletedAsync(hostId, completionId, ct);
        }
    }

    private static CommunityOperationOutcome.Invalid? Validate(CommunitySeasonDraft draft) =>
        draft.OperationId == Guid.Empty ? new("An operation ID is required.")
        : string.IsNullOrWhiteSpace(draft.Name) || draft.Name.Trim().Length > 160
            ? new("Season name must be between 1 and 160 characters.")
        : draft.Description.Length > 2000 || draft.ModeratorNotes.Length > 2000
            ? new("Season description and moderator notes must be 2,000 characters or fewer.")
        : EnsureUtc(draft.StartsAtUtc) >= EnsureUtc(draft.EndsAtUtc)
            ? new("Season end must be after its start.")
        : Validate(draft.Actor);

    private static CommunityOperationOutcome.Invalid? Validate(CommunityRewardDraft draft)
    {
        var key = NormalizeKey(draft.Key);
        return draft.OperationId == Guid.Empty ? new("An operation ID is required.")
            : !ValidKey(key)
                ? new(
                    "Reward key must start with a letter and contain only lowercase letters, digits, and hyphens."
                )
            : string.IsNullOrWhiteSpace(draft.Name) || draft.Name.Trim().Length > 160
                ? new("Reward name must be between 1 and 160 characters.")
            : !CommunityPresentationCatalog.Supports(
                draft.Kind,
                draft.PresentationToken.Trim().ToLowerInvariant()
            )
                ? new("Choose a supported presentation token for this reward type.")
            : Validate(draft.Actor);
    }

    private static CommunityOperationOutcome.Invalid? Validate(CommunityDefinitionDraft draft)
    {
        var key = NormalizeKey(draft.Key);
        var rule = CommunityEventRuleCatalog.Describe(draft.EventRule);
        return draft.OperationId == Guid.Empty ? new("An operation ID is required.")
            : !ValidKey(key)
                ? new(
                    "Definition key must start with a letter and contain only lowercase letters, digits, and hyphens."
                )
            : string.IsNullOrWhiteSpace(draft.Name) || draft.Name.Trim().Length > 160
                ? new("Definition name must be between 1 and 160 characters.")
            : draft.Target <= 0 ? new("Progress target must be positive.")
            : draft.EventRule == CommunityEventRuleKind.ExternalGrant
            && (
                draft.Kind != CommunityDefinitionKind.Achievement
                || draft.Scope != CommunityProgressScope.Viewer
                || draft.CompletionMode != CommunityCompletionMode.OneTime
            )
                ? new("External grants are only supported for one-time viewer achievements.")
            : draft.Scope == CommunityProgressScope.Viewer && !rule.SupportsViewerProgress
                ? new("That supported event rule cannot identify a viewer subject.")
            : draft.Scope == CommunityProgressScope.Communal && !rule.SupportsCommunalProgress
                ? new("That supported event rule requires a viewer subject.")
            : draft.Increment == CommunityProgressIncrement.EventValue && !rule.SupportsEventValue
                ? new("That supported event rule has no numeric event value.")
            : !string.IsNullOrWhiteSpace(draft.FilterToken) && !rule.SupportsFilter
                ? new("That supported event rule does not accept a filter token.")
            : draft.Kind == CommunityDefinitionKind.Achievement
            && draft.CompletionMode != CommunityCompletionMode.OneTime
                ? new("Achievements are one-time completions.")
            : draft.Scope == CommunityProgressScope.Communal && !draft.PointsReward.IsZero
                ? new("Communal goals cannot grant viewer points without a viewer subject.")
            : draft.Scope == CommunityProgressScope.Communal && draft.Rewards.Count > 0
                ? new(
                    "Communal goals cannot grant viewer profile rewards without a viewer subject."
                )
            : draft.CompletionMode == CommunityCompletionMode.OneTime
            && draft.ResetSchedule.Cadence != CommunityResetCadence.None
                ? new("Only repeatable definitions use reset schedules.")
            : Validate(draft.ResetSchedule) ?? Validate(draft.Actor);
    }

    private static CommunityOperationOutcome.Invalid? Validate(CommunityResetSchedule schedule) =>
        schedule.Cadence switch
        {
            CommunityResetCadence.None when schedule.Weekday is not null => new(
                "A schedule without resets cannot choose a weekday."
            ),
            CommunityResetCadence.Daily when schedule.Weekday is not null => new(
                "Daily resets do not choose a weekday."
            ),
            CommunityResetCadence.Weekly when schedule.Weekday is null => new(
                "Weekly resets require a weekday."
            ),
            _ => null,
        };

    private static CommunityOperationOutcome.Invalid? Validate(CommunitySourceEvent sourceEvent) =>
        string.IsNullOrWhiteSpace(sourceEvent.SourceEventId)
        || sourceEvent.SourceEventId.Length > 200
            ? new("Source event ID must be between 1 and 200 characters.")
        : sourceEvent.Value <= 0 ? new("Source event values must be positive.")
        : sourceEvent.Viewer is null ? null
        : Validate(sourceEvent.Viewer);

    private static CommunityOperationOutcome.Invalid? Validate(CommunityViewer viewer) =>
        string.IsNullOrWhiteSpace(viewer.TwitchUserId) || viewer.TwitchUserId.Length > 128
            ? new("A bounded Twitch user ID is required.")
        : !CommunityInput.IsValidLogin(CommunityInput.NormalizeLogin(viewer.Login))
            ? new("A valid Twitch login is required.")
        : string.IsNullOrWhiteSpace(viewer.DisplayName) || viewer.DisplayName.Length > 160
            ? new("A bounded Twitch display name is required.")
        : null;

    private static CommunityOperationOutcome.Invalid? Validate(CommunityActor actor) =>
        string.IsNullOrWhiteSpace(actor.TwitchUserId) || actor.TwitchUserId.Length > 128
            ? new("A bounded Twitch actor ID is required.")
        : !CommunityInput.IsValidLogin(CommunityInput.NormalizeLogin(actor.Login))
            ? new("A valid Twitch actor login is required.")
        : null;

    private static CommunityOperationOutcome.Invalid? Validate(
        CommunityExternalGrantRequest request
    ) =>
        string.IsNullOrWhiteSpace(request.Source) || request.Source.Length > 80
            ? new("External grant source must be between 1 and 80 characters.")
        : string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 200
            ? new("External grant idempotency key must be between 1 and 200 characters.")
        : Validate(request.Viewer);

    private static CommunityActor Normalize(CommunityActor actor) =>
        new(actor.TwitchUserId.Trim(), CommunityInput.NormalizeLogin(actor.Login));

    private static CommunityViewer Normalize(CommunityViewer viewer) =>
        new(
            viewer.TwitchUserId.Trim(),
            CommunityInput.NormalizeLogin(viewer.Login),
            viewer.DisplayName.Trim()
        );

    private static string NormalizeKey(string value) => value.Trim().ToLowerInvariant();

    private static bool ValidKey(string value) =>
        value.Length is >= 1 and <= 80
        && value[0] is >= 'a' and <= 'z'
        && value.All(character => character is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-');

    private static string? NormalizeFilter(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();

    private static bool FilterMatches(string? configured, string? actual) =>
        configured is null
        || string.Equals(configured, NormalizeFilter(actual), StringComparison.Ordinal);

    private static string? EquippedRewardKey(string publicPayload)
    {
        try
        {
            using var document = JsonDocument.Parse(publicPayload);
            return document.RootElement.TryGetProperty("rewardKey", out var rewardKey)
                ? rewardKey.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static DateTime EnsureUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();

    private static CommunityAudit Audit(
        int hostId,
        CommunitySeason season,
        CommunityDefinition? definition,
        string action,
        string operationKey,
        CommunityActor actor,
        string privateNote,
        DateTime now
    ) =>
        new()
        {
            HostId = hostId,
            SeasonId = season.Id,
            DefinitionId = definition?.Id,
            Action = action,
            OperationKey = operationKey,
            ActorTwitchUserId = actor.TwitchUserId,
            ActorLogin = actor.Login,
            PrivateNote = privateNote.Trim(),
            OccurredAtUtc = now,
        };

    private static CommunityDomainEvent DomainEvent(
        int hostId,
        long seasonId,
        CommunityEventKind kind,
        string operationKey,
        string publicPayload,
        DateTime now
    ) =>
        new()
        {
            HostId = hostId,
            SeasonId = seasonId,
            Kind = kind,
            OperationKey = operationKey,
            PublicPayload = publicPayload,
            OccurredAtUtc = now,
        };

    private static string Fingerprint(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static async Task<T> RetryAsync<T>(Func<Task<T>> operation, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await operation();
            }
            catch (Exception exception)
                when (attempt < _persistenceRetryCount && IsPersistenceCollision(exception))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(attempt * 5), ct);
            }
        }
    }

    private static bool IsPersistenceCollision(Exception exception) =>
        exception switch
        {
            SqliteException
            {
                SqliteErrorCode: SQLitePCL.raw.SQLITE_BUSY or SQLitePCL.raw.SQLITE_LOCKED,
            } => true,
            SqliteException
            {
                SqliteErrorCode: SQLitePCL.raw.SQLITE_CONSTRAINT,
                SqliteExtendedErrorCode: SQLitePCL.raw.SQLITE_CONSTRAINT_UNIQUE,
            } => true,
            DbUpdateException { InnerException: { } inner } => IsPersistenceCollision(inner),
            _ => false,
        };
}
