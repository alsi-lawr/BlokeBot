using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BlokeBot.Core.Features.CommunityProgression;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Core.Identity;
using BlokeBot.Eventing;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Bingo;

public sealed class BingoService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    ICommunityAchievementGrantService achievementGrants,
    EventBus<AppEventKind> events,
    TimeProvider clock,
    IEnumerable<IBingoOverlayEventObserver>? overlayObservers = null
)
{
    private const int _persistenceRetryCount = 20;
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _hostGates = new();
    private readonly IBingoOverlayEventObserver[] _overlayObservers = [.. overlayObservers ?? []];

    public async Task<BingoOperationOutcome> SaveTemplateAsync(
        int hostId,
        BingoTemplateDraft draft,
        CancellationToken ct
    ) =>
        await LockedAsync<BingoOperationOutcome>(
            hostId,
            async () =>
            {
                if (Validate(draft) is { } invalid)
                {
                    return invalid;
                }
                await using var db = await dbFactory.CreateDbContextAsync(ct);
                if (!await FeatureEnabledAsync(db, hostId, ct))
                {
                    return new BingoOperationOutcome.FeatureDisabled();
                }
                if (!await RewardDefinitionsExistAsync(db, hostId, draft, ct))
                {
                    return new BingoOperationOutcome.Invalid(
                        "Achievement rewards must reference predeclared viewer achievements that accept external grants."
                    );
                }
                if (!await CounterDefinitionsExistAsync(db, hostId, draft.Squares, ct))
                {
                    return new BingoOperationOutcome.Invalid(
                        "Counter squares must reference a saved counter for this channel."
                    );
                }
                if (
                    await db
                        .BingoTemplateRevisions.AsNoTracking()
                        .AnyAsync(
                            value =>
                                value.HostId == hostId && value.OperationId == draft.OperationId,
                            ct
                        )
                )
                {
                    return new BingoOperationOutcome.Succeeded(true);
                }

                var now = clock.GetUtcNow().UtcDateTime;
                BingoTemplate template;
                if (draft.TemplateId is null)
                {
                    template = new BingoTemplate
                    {
                        HostId = hostId,
                        PublicId = Guid.NewGuid(),
                        CreationOperationId = draft.OperationId,
                        Name = NormalizeText(draft.Name),
                        CurrentRevision = 1,
                        CreatedAtUtc = now,
                        UpdatedAtUtc = now,
                    };
                    _ = db.BingoTemplates.Add(template);
                }
                else
                {
                    var existing = await db.BingoTemplates.SingleOrDefaultAsync(
                        value =>
                            value.HostId == hostId
                            && value.PublicId == draft.TemplateId.Value.Value,
                        ct
                    );
                    if (existing is null)
                    {
                        return new BingoOperationOutcome.NotFound();
                    }
                    template = existing;
                    template.Name = NormalizeText(draft.Name);
                    template.CurrentRevision++;
                    template.UpdatedAtUtc = now;
                }

                var revision = new BingoTemplateRevision
                {
                    HostId = hostId,
                    OperationId = draft.OperationId,
                    Template = template,
                    Revision = template.CurrentRevision,
                    Dimension = draft.Dimension.Value,
                    FullCardWinEnabled = draft.FullCardWinEnabled,
                    LinePointsReward = draft.LineReward.Points.ToString(),
                    LineAchievementKey = draft.LineReward.AchievementKey?.Value,
                    FullCardPointsReward = draft.FullCardReward.Points.ToString(),
                    FullCardAchievementKey = draft.FullCardReward.AchievementKey?.Value,
                    CreatedByTwitchUserId = NormalizeIdentity(draft.Actor.TwitchUserId),
                    CreatedByLogin = NormalizeLogin(draft.Actor.Login),
                    CreatedAtUtc = now,
                };
                revision.Squares.AddRange(
                    draft.Squares.Select((definition, index) => ToEntity(hostId, definition, index))
                );
                _ = db.BingoTemplateRevisions.Add(revision);
                _ = await db.SaveChangesAsync(ct);
                await PublishChangedAsync(ct);
                return new BingoOperationOutcome.Succeeded();
            },
            ct
        );

    public async Task<BingoOperationOutcome> CreateGameAsync(
        int hostId,
        BingoGameDraft draft,
        CancellationToken ct
    ) =>
        await LockedLifecycleAsync<BingoOperationOutcome>(
            hostId,
            async () =>
            {
                if (Validate(draft) is { } invalid)
                {
                    return invalid;
                }
                await using var db = await dbFactory.CreateDbContextAsync(ct);
                var host = await db.Hosts.SingleOrDefaultAsync(value => value.Id == hostId, ct);
                if (host is null || !host.EnabledFeatures.Contains(HostFeatureFlags.Bingo))
                {
                    return new BingoOperationOutcome.FeatureDisabled();
                }
                if (
                    await db
                        .BingoGames.AsNoTracking()
                        .AnyAsync(
                            value =>
                                value.HostId == hostId
                                && value.CreationOperationId == draft.OperationId,
                            ct
                        )
                )
                {
                    return new BingoOperationOutcome.Succeeded(true);
                }
                if (
                    await db
                        .BingoGames.AsNoTracking()
                        .AnyAsync(
                            value =>
                                value.HostId == hostId
                                && (
                                    value.Status == BingoGameStatus.Joining
                                    || value.Status == BingoGameStatus.Issued
                                ),
                            ct
                        )
                )
                {
                    return new BingoOperationOutcome.Conflict(
                        "Archive the current Bingo game before creating another."
                    );
                }
                var template = await db
                    .BingoTemplates.AsNoTracking()
                    .Where(value =>
                        value.HostId == hostId && value.PublicId == draft.TemplateId.Value
                    )
                    .Select(value => new
                    {
                        value.Id,
                        value.Name,
                        value.CurrentRevision,
                    })
                    .SingleOrDefaultAsync(ct);
                if (template is null)
                {
                    return new BingoOperationOutcome.NotFound();
                }
                var revision = await db.BingoTemplateRevisions.SingleAsync(
                    value =>
                        value.TemplateId == template.Id
                        && value.Revision == template.CurrentRevision,
                    ct
                );
                var rewardsConfigured =
                    !PointAmount.ParseAbsolute(revision.LinePointsReward).IsZero
                    || !PointAmount.ParseAbsolute(revision.FullCardPointsReward).IsZero;
                if (rewardsConfigured && !host.EnabledFeatures.Contains(HostFeatureFlags.Points))
                {
                    return new BingoOperationOutcome.Invalid(
                        "Turn Points on before starting a Bingo template with point rewards."
                    );
                }
                var progressionConfigured =
                    revision.LineAchievementKey is not null
                    || revision.FullCardAchievementKey is not null;
                if (
                    progressionConfigured
                    && !host.EnabledFeatures.Contains(HostFeatureFlags.CommunityProgression)
                )
                {
                    return new BingoOperationOutcome.Invalid(
                        "Turn Community progression on before starting a Bingo template with achievement or title rewards."
                    );
                }

                var now = clock.GetUtcNow().UtcDateTime;
                var game = new BingoGame
                {
                    HostId = hostId,
                    PublicId = Guid.NewGuid(),
                    CreationOperationId = draft.OperationId,
                    TemplateRevisionId = revision.Id,
                    TemplateName = template.Name,
                    TemplateRevisionNumber = revision.Revision,
                    Dimension = revision.Dimension,
                    Seed = draft.Seed.Trim(),
                    Mode = draft.Mode,
                    Status = BingoGameStatus.Joining,
                    ParticipantCap = draft.ParticipantCap,
                    TeamCap = draft.TeamCap,
                    FullCardWinEnabled = revision.FullCardWinEnabled,
                    LinePointsReward = revision.LinePointsReward,
                    LineAchievementKey = revision.LineAchievementKey,
                    FullCardPointsReward = revision.FullCardPointsReward,
                    FullCardAchievementKey = revision.FullCardAchievementKey,
                    CreatedAtUtc = now,
                };
                game.Teams.AddRange(
                    draft.Teams.Select(
                        (name, index) =>
                            new BingoTeam
                            {
                                HostId = hostId,
                                PublicId = Guid.NewGuid(),
                                Name = NormalizeText(name),
                                SortOrder = index,
                            }
                    )
                );
                _ = db.BingoGames.Add(game);
                _ = await db.SaveChangesAsync(ct);
                await PublishChangedAsync(ct);
                return new BingoOperationOutcome.Succeeded();
            },
            ct
        );

    public Task<BingoOperationOutcome> JoinAsync(
        int hostId,
        BingoRosterCommand command,
        CancellationToken ct
    ) => ChangeRosterAsync(hostId, command, RosterChange.Join, ct);

    public Task<BingoOperationOutcome> MoveAsync(
        int hostId,
        BingoRosterCommand command,
        CancellationToken ct
    ) => ChangeRosterAsync(hostId, command, RosterChange.Move, ct);

    public Task<BingoOperationOutcome> RemoveAsync(
        int hostId,
        BingoRosterCommand command,
        CancellationToken ct
    ) => ChangeRosterAsync(hostId, command, RosterChange.Remove, ct);

    public async Task<BingoOperationOutcome> IssueAsync(
        int hostId,
        BingoGameActionCommand command,
        CancellationToken ct
    ) =>
        await LockedLifecycleAsync<BingoOperationOutcome>(
            hostId,
            async () =>
            {
                await using var db = await dbFactory.CreateDbContextAsync(ct);
                await using var transaction = await db.Database.BeginTransactionAsync(ct);
                if (!await FeatureEnabledAsync(db, hostId, ct))
                {
                    return new BingoOperationOutcome.FeatureDisabled();
                }
                if (await OperationRecordedAsync(db, hostId, command.OperationId, ct))
                {
                    return new BingoOperationOutcome.Succeeded(true);
                }
                var game = await db
                    .BingoGames.Include(value => value.Teams)
                    .Include(value => value.Participants)
                    .SingleOrDefaultAsync(
                        value => value.HostId == hostId && value.PublicId == command.GameId.Value,
                        ct
                    );
                if (game is null)
                {
                    return new BingoOperationOutcome.NotFound();
                }
                if (game.Status != BingoGameStatus.Joining)
                {
                    return new BingoOperationOutcome.Frozen();
                }
                if (game.Mode != BingoGameMode.Shared && game.Participants.Count == 0)
                {
                    return new BingoOperationOutcome.Invalid(
                        "Unique and team games need at least one participant before cards are issued."
                    );
                }
                if (
                    game.Mode == BingoGameMode.Team
                    && game.Participants.Any(value => value.TeamId is null)
                )
                {
                    return new BingoOperationOutcome.Invalid(
                        "Every participant needs a team before cards are issued."
                    );
                }

                var now = clock.GetUtcNow().UtcDateTime;
                var cards = CreateCards(game, hostId, now);
                db.BingoCards.AddRange(cards);
                game.Status = BingoGameStatus.Issued;
                game.IssuedAtUtc = now;
                game.RosterRevision++;
                AddAudit(
                    db,
                    hostId,
                    game.Id,
                    null,
                    null,
                    command.OperationId,
                    "issue",
                    command.Actor,
                    command.PrivateNote,
                    now
                );
                AddDomainEvent(
                    db,
                    game,
                    null,
                    BingoDomainEventKind.GameIssued,
                    $"issue:{command.OperationId:N}",
                    "Bingo cards issued",
                    now
                );
                _ = await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                await PublishAndOverlayAsync(
                    new(
                        hostId,
                        command.GameId,
                        null,
                        BingoDomainEventKind.GameIssued,
                        $"issue:{command.OperationId:N}",
                        "Bingo cards issued",
                        now
                    ),
                    ct
                );
                return new BingoOperationOutcome.Succeeded();
            },
            ct
        );

    public Task<BingoOperationOutcome> ConfirmManualAsync(
        int hostId,
        BingoManualMarkCommand command,
        CancellationToken ct
    ) => ChangeManualMarkAsync(hostId, command, mark: true, ct);

    public Task<BingoOperationOutcome> ReverseManualAsync(
        int hostId,
        BingoManualMarkCommand command,
        CancellationToken ct
    ) => ChangeManualMarkAsync(hostId, command, mark: false, ct);

    public async Task<BingoOperationOutcome> ProcessEventAsync(
        int hostId,
        BingoAutomaticEvent sourceEvent,
        CancellationToken ct
    ) =>
        await LockedAsync<BingoOperationOutcome>(
            hostId,
            async () =>
            {
                await using var db = await dbFactory.CreateDbContextAsync(ct);
                var host = await db
                    .Hosts.AsNoTracking()
                    .SingleOrDefaultAsync(value => value.Id == hostId, ct);
                if (host is null || !host.EnabledFeatures.Contains(HostFeatureFlags.Bingo))
                {
                    return new BingoOperationOutcome.FeatureDisabled();
                }
                if (
                    host.BingoAcceptEventsAfterUtc is { } acceptAfter
                    && sourceEvent.OccurredAtUtc.UtcDateTime <= acceptAfter
                )
                {
                    return new BingoOperationOutcome.Succeeded();
                }
                if (
                    await db
                        .BingoEventReceipts.AsNoTracking()
                        .AnyAsync(
                            value =>
                                value.HostId == hostId
                                && value.Kind == sourceEvent.Kind
                                && value.SourceEventId == sourceEvent.SourceEventId,
                            ct
                        )
                )
                {
                    return new BingoOperationOutcome.Succeeded(true);
                }

                var game = await LoadIssuedGameAsync(db, hostId, ct);
                _ = db.BingoEventReceipts.Add(
                    new BingoEventReceipt
                    {
                        HostId = hostId,
                        GameId = game?.Id,
                        Kind = sourceEvent.Kind,
                        SourceEventId = sourceEvent.SourceEventId,
                        OccurredAtUtc = sourceEvent.OccurredAtUtc.UtcDateTime,
                        RecordedAtUtc = clock.GetUtcNow().UtcDateTime,
                    }
                );
                if (game is null)
                {
                    _ = await db.SaveChangesAsync(ct);
                    return new BingoOperationOutcome.Succeeded();
                }
                var matchingKeys = game.TemplateRevision!.Squares.Where(value =>
                        Matches(value, sourceEvent)
                    )
                    .Select(value => value.Key)
                    .ToHashSet(StringComparer.Ordinal);
                if (matchingKeys.Count == 0)
                {
                    _ = await db.SaveChangesAsync(ct);
                    return new BingoOperationOutcome.Succeeded();
                }

                var now = clock.GetUtcNow().UtcDateTime;
                var changedCards = new List<BingoCard>();
                foreach (var card in game.Cards)
                {
                    var layout = Layout(game, card);
                    var changed = false;
                    foreach (var squareKey in matchingKeys)
                    {
                        var position = IndexOf(layout, squareKey);
                        var mark = card.Marks.SingleOrDefault(value =>
                            value.SquareKey == squareKey
                        );
                        if (mark?.IsActive == true)
                        {
                            continue;
                        }
                        mark ??= AddMark(db, game, card, squareKey, position, now);
                        mark.IsActive = true;
                        mark.ChangedAtUtc = now;
                        _ = db.BingoEvidence.Add(ToEvidence(game, card, mark, sourceEvent, now));
                        changed = true;
                    }
                    if (changed)
                    {
                        changedCards.Add(card);
                        AddDomainEvent(
                            db,
                            game,
                            card,
                            BingoDomainEventKind.SquareMarked,
                            $"event:{sourceEvent.Kind}:{sourceEvent.SourceEventId}:{card.PublicId:N}",
                            sourceEvent.PublicSummary,
                            now
                        );
                        if (!await DetectWinsAsync(db, game, card, now, ct))
                        {
                            return new BingoOperationOutcome.Conflict(
                                "A Bingo point reward would exceed the supported balance limit."
                            );
                        }
                    }
                }
                _ = await db.SaveChangesAsync(ct);
                await GrantPendingAchievementsAsync(hostId, ct);
                if (changedCards.Count > 0)
                {
                    await PublishAndOverlayAsync(
                        new(
                            hostId,
                            new(game.PublicId),
                            null,
                            BingoDomainEventKind.SquareMarked,
                            $"event:{sourceEvent.Kind}:{sourceEvent.SourceEventId}",
                            sourceEvent.PublicSummary,
                            sourceEvent.OccurredAtUtc
                        ),
                        ct
                    );
                }
                return new BingoOperationOutcome.Succeeded();
            },
            ct
        );

    public async Task<BingoOperationOutcome> ArchiveAsync(
        int hostId,
        BingoGameActionCommand command,
        CancellationToken ct
    ) =>
        await LockedAsync<BingoOperationOutcome>(
            hostId,
            async () =>
            {
                await using var db = await dbFactory.CreateDbContextAsync(ct);
                if (!await FeatureEnabledAsync(db, hostId, ct))
                {
                    return new BingoOperationOutcome.FeatureDisabled();
                }
                if (await OperationRecordedAsync(db, hostId, command.OperationId, ct))
                {
                    return new BingoOperationOutcome.Succeeded(true);
                }
                var game = await db.BingoGames.SingleOrDefaultAsync(
                    value => value.HostId == hostId && value.PublicId == command.GameId.Value,
                    ct
                );
                if (game is null)
                {
                    return new BingoOperationOutcome.NotFound();
                }
                if (game.Status == BingoGameStatus.Joining)
                {
                    return new BingoOperationOutcome.Invalid(
                        "Issue cards before archiving this game."
                    );
                }
                if (game.Status == BingoGameStatus.Archived)
                {
                    return new BingoOperationOutcome.Succeeded(true);
                }
                var now = clock.GetUtcNow().UtcDateTime;
                game.Status = BingoGameStatus.Archived;
                game.CompletedAtUtc ??= now;
                game.ArchivedAtUtc = now;
                AddAudit(
                    db,
                    hostId,
                    game.Id,
                    null,
                    null,
                    command.OperationId,
                    "archive",
                    command.Actor,
                    command.PrivateNote,
                    now
                );
                AddDomainEvent(
                    db,
                    game,
                    null,
                    BingoDomainEventKind.GameArchived,
                    $"archive:{command.OperationId:N}",
                    "Bingo game archived",
                    now
                );
                _ = await db.SaveChangesAsync(ct);
                await PublishAndOverlayAsync(
                    new(
                        hostId,
                        command.GameId,
                        null,
                        BingoDomainEventKind.GameArchived,
                        $"archive:{command.OperationId:N}",
                        "Bingo game archived",
                        now
                    ),
                    ct
                );
                return new BingoOperationOutcome.Succeeded();
            },
            ct
        );

    public async Task<IReadOnlyList<BingoTemplateView>> GetTemplatesAsync(
        int hostId,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await FeatureEnabledAsync(db, hostId, ct))
        {
            return [];
        }
        var templates = await db
            .BingoTemplates.AsNoTracking()
            .Where(value => value.HostId == hostId)
            .OrderBy(value => value.Name)
            .ToArrayAsync(ct);
        var result = new List<BingoTemplateView>(templates.Length);
        foreach (var template in templates)
        {
            var revision = await db
                .BingoTemplateRevisions.AsNoTracking()
                .Include(value => value.Squares)
                .SingleAsync(
                    value =>
                        value.TemplateId == template.Id
                        && value.Revision == template.CurrentRevision,
                    ct
                );
            result.Add(ToView(template, revision));
        }
        return result;
    }

    public async Task<IReadOnlyList<BingoModeratorGameView>> GetModeratorGamesAsync(
        int hostId,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await FeatureEnabledAsync(db, hostId, ct))
        {
            return [];
        }
        var games = await LoadGamesAsync(db, hostId, includeJoining: true, ct);
        var result = new List<BingoModeratorGameView>(games.Count);
        foreach (var game in games)
        {
            var audits = await db
                .BingoModerationAudit.AsNoTracking()
                .Where(value => value.HostId == hostId && value.GameId == game.Id)
                .OrderBy(value => value.OccurredAtUtc)
                .Select(value => new BingoModeratorAuditView(
                    value.Action,
                    value.ActorLogin,
                    value.PrivateNote,
                    value.OccurredAtUtc
                ))
                .ToArrayAsync(ct);
            result.Add(new(await ToViewAsync(db, game, ct), audits));
        }
        return result;
    }

    public async Task<BingoPublicView?> GetPublicAsync(string hostLogin, CancellationToken ct)
    {
        var login = NormalizeLogin(hostLogin);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var host = await db
            .Hosts.AsNoTracking()
            .Where(value =>
                value.Login == login
                && (value.EnabledFeatures & HostFeatureFlags.Bingo) == HostFeatureFlags.Bingo
            )
            .Select(value => new { value.Id, value.Login })
            .SingleOrDefaultAsync(ct);
        if (host is null)
        {
            return null;
        }
        var games = await LoadGamesAsync(db, host.Id, includeJoining: false, ct);
        var liveEntity = games.FirstOrDefault(value => value.Status == BingoGameStatus.Issued);
        var archiveEntities = games
            .Where(value => value.Status == BingoGameStatus.Archived)
            .ToArray();
        var live = liveEntity is null ? null : await ToViewAsync(db, liveEntity, ct);
        var archive = new List<BingoGameView>(archiveEntities.Length);
        foreach (var game in archiveEntities)
        {
            archive.Add(await ToViewAsync(db, game, ct));
        }
        return new(host.Login, live, archive);
    }

    public async Task ReconcilePendingRewardsAsync(int hostId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await FeatureEnabledAsync(db, hostId, ct))
        {
            return;
        }
        await GrantPendingAchievementsAsync(hostId, ct);
    }

    private async Task<BingoOperationOutcome> ChangeRosterAsync(
        int hostId,
        BingoRosterCommand command,
        RosterChange change,
        CancellationToken ct
    ) =>
        await LockedLifecycleAsync<BingoOperationOutcome>(
            hostId,
            async () =>
            {
                await using var db = await dbFactory.CreateDbContextAsync(ct);
                if (!await FeatureEnabledAsync(db, hostId, ct))
                {
                    return new BingoOperationOutcome.FeatureDisabled();
                }
                if (await OperationRecordedAsync(db, hostId, command.OperationId, ct))
                {
                    return new BingoOperationOutcome.Succeeded(true);
                }
                var game = await db
                    .BingoGames.Include(value => value.Teams)
                    .Include(value => value.Participants)
                    .SingleOrDefaultAsync(
                        value => value.HostId == hostId && value.PublicId == command.GameId.Value,
                        ct
                    );
                if (game is null)
                {
                    return new BingoOperationOutcome.NotFound();
                }
                if (game.Status != BingoGameStatus.Joining)
                {
                    return new BingoOperationOutcome.Frozen();
                }
                var viewerId = NormalizeIdentity(command.Viewer.TwitchUserId);
                var participant = game.Participants.SingleOrDefault(value =>
                    value.TwitchUserId == viewerId
                );
                var team = command.TeamId is null
                    ? null
                    : game.Teams.SingleOrDefault(value =>
                        value.PublicId == command.TeamId.Value.Value
                    );
                if (command.TeamId is not null && team is null)
                {
                    return new BingoOperationOutcome.NotFound();
                }
                if (game.Mode != BingoGameMode.Team && team is not null)
                {
                    return new BingoOperationOutcome.Invalid(
                        "Only team games assign participants to teams."
                    );
                }
                if (
                    game.Mode == BingoGameMode.Team
                    && change != RosterChange.Remove
                    && team is null
                )
                {
                    return new BingoOperationOutcome.Invalid("Choose a team.");
                }
                if (
                    change == RosterChange.Join
                    && participant is null
                    && game.ParticipantCap is { } cap
                    && game.Participants.Count >= cap
                )
                {
                    return new BingoOperationOutcome.Invalid(
                        "The participant cap has been reached."
                    );
                }
                var now = clock.GetUtcNow().UtcDateTime;
                switch (change)
                {
                    case RosterChange.Join:
                        if (participant is null)
                        {
                            participant = new BingoParticipant
                            {
                                HostId = hostId,
                                GameId = game.Id,
                                TeamId = team?.Id,
                                TwitchUserId = viewerId,
                                Login = NormalizeLogin(command.Viewer.Login),
                                DisplayName = NormalizeDisplayName(command.Viewer),
                                JoinedAtUtc = now,
                            };
                            _ = db.BingoParticipants.Add(participant);
                        }
                        else
                        {
                            participant.TeamId = team?.Id;
                            participant.Login = NormalizeLogin(command.Viewer.Login);
                            participant.DisplayName = NormalizeDisplayName(command.Viewer);
                        }
                        break;
                    case RosterChange.Move:
                        if (participant is null)
                        {
                            return new BingoOperationOutcome.NotFound();
                        }
                        participant.TeamId = team?.Id;
                        break;
                    case RosterChange.Remove:
                        if (participant is null)
                        {
                            return new BingoOperationOutcome.Succeeded(true);
                        }
                        _ = db.BingoParticipants.Remove(participant);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(change));
                }
                game.RosterRevision++;
                AddAudit(
                    db,
                    hostId,
                    game.Id,
                    null,
                    null,
                    command.OperationId,
                    change.ToString().ToLowerInvariant(),
                    command.Actor,
                    command.PrivateNote,
                    now
                );
                _ = await db.SaveChangesAsync(ct);
                await PublishChangedAsync(ct);
                return new BingoOperationOutcome.Succeeded();
            },
            ct
        );

    private async Task<BingoOperationOutcome> ChangeManualMarkAsync(
        int hostId,
        BingoManualMarkCommand command,
        bool mark,
        CancellationToken ct
    ) =>
        await LockedAsync<BingoOperationOutcome>(
            hostId,
            async () =>
            {
                await using var db = await dbFactory.CreateDbContextAsync(ct);
                if (!await FeatureEnabledAsync(db, hostId, ct))
                {
                    return new BingoOperationOutcome.FeatureDisabled();
                }
                if (await OperationRecordedAsync(db, hostId, command.OperationId, ct))
                {
                    return new BingoOperationOutcome.Succeeded(true);
                }
                var game = await LoadIssuedGameAsync(db, hostId, ct);
                if (game is null || game.PublicId != command.GameId.Value)
                {
                    return new BingoOperationOutcome.NotFound();
                }
                var card = game.Cards.SingleOrDefault(value =>
                    value.PublicId == command.CardId.Value
                );
                if (card is null)
                {
                    return new BingoOperationOutcome.NotFound();
                }
                var layout = Layout(game, card);
                if (command.Position < 0 || command.Position >= layout.Count)
                {
                    return new BingoOperationOutcome.Invalid("Choose a square on the issued card.");
                }
                var squareKey = layout[command.Position].Value;
                var definition = game.TemplateRevision!.Squares.Single(value =>
                    value.Key == squareKey
                );
                if (definition.Kind != BingoSquareKind.Manual)
                {
                    return new BingoOperationOutcome.Invalid(
                        "Automatic squares can only be marked by their typed event source."
                    );
                }
                var now = clock.GetUtcNow().UtcDateTime;
                var current = card.Marks.SingleOrDefault(value => value.SquareKey == squareKey);
                if ((mark && current?.IsActive == true) || (!mark && current?.IsActive != true))
                {
                    AddAudit(
                        db,
                        hostId,
                        game.Id,
                        card.Id,
                        current?.Id,
                        command.OperationId,
                        mark ? "confirm-idempotent" : "reverse-idempotent",
                        command.Actor,
                        command.PrivateNote,
                        now
                    );
                    _ = await db.SaveChangesAsync(ct);
                    return new BingoOperationOutcome.Succeeded(true);
                }
                current ??= AddMark(db, game, card, squareKey, command.Position, now);
                current.IsActive = mark;
                current.ChangedAtUtc = now;
                _ = db.BingoEvidence.Add(
                    new BingoEvidence
                    {
                        HostId = hostId,
                        GameId = game.Id,
                        CardId = card.Id,
                        Mark = current,
                        Action = mark ? BingoEvidenceAction.Marked : BingoEvidenceAction.Reversed,
                        Source = BingoEvidenceSource.Manual,
                        EventKind = BingoSquareKind.Manual,
                        Summary = mark
                            ? "Moderator confirmed this square"
                            : "Moderator reversed this square",
                        OccurredAtUtc = now,
                        RecordedAtUtc = now,
                    }
                );
                AddAudit(
                    db,
                    hostId,
                    game.Id,
                    card.Id,
                    current.Id == 0 ? null : current.Id,
                    command.OperationId,
                    mark ? "confirm" : "reverse",
                    command.Actor,
                    command.PrivateNote,
                    now
                );
                AddDomainEvent(
                    db,
                    game,
                    card,
                    mark ? BingoDomainEventKind.SquareMarked : BingoDomainEventKind.SquareReversed,
                    $"manual:{command.OperationId:N}",
                    mark
                        ? "Moderator confirmed a Bingo square"
                        : "Moderator reversed a Bingo square",
                    now
                );
                if (mark)
                {
                    if (!await DetectWinsAsync(db, game, card, now, ct))
                    {
                        return new BingoOperationOutcome.Conflict(
                            "A Bingo point reward would exceed the supported balance limit."
                        );
                    }
                }
                _ = await db.SaveChangesAsync(ct);
                await GrantPendingAchievementsAsync(hostId, ct);
                await PublishAndOverlayAsync(
                    new(
                        hostId,
                        command.GameId,
                        command.CardId,
                        mark
                            ? BingoDomainEventKind.SquareMarked
                            : BingoDomainEventKind.SquareReversed,
                        $"manual:{command.OperationId:N}",
                        mark
                            ? "Moderator confirmed a Bingo square"
                            : "Moderator reversed a Bingo square",
                        now
                    ),
                    ct
                );
                return new BingoOperationOutcome.Succeeded();
            },
            ct
        );

    private async Task<bool> DetectWinsAsync(
        BlokeBotDbContext db,
        BingoGame game,
        BingoCard card,
        DateTime now,
        CancellationToken ct
    )
    {
        var activePositions = card
            .Marks.Where(value => value.IsActive)
            .Select(value => value.Position)
            .ToHashSet();
        var existingRules = card
            .Wins.Select(value => value.RuleKey)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var line in BingoCardLayout.WinLines(new(game.Dimension), game.FullCardWinEnabled))
        {
            if (
                existingRules.Contains(line.RuleKey)
                || !line.Positions.All(activePositions.Contains)
            )
            {
                continue;
            }
            var isFullCard = line.Kind == BingoWinKind.FullCard;
            var win = new BingoWin
            {
                HostId = game.HostId,
                GameId = game.Id,
                CardId = card.Id,
                PublicId = Guid.NewGuid(),
                Kind = line.Kind,
                RuleIndex = line.Index,
                RuleKey = line.RuleKey,
                PointsReward = isFullCard ? game.FullCardPointsReward : game.LinePointsReward,
                AchievementKey = isFullCard ? game.FullCardAchievementKey : game.LineAchievementKey,
                CompletedAtUtc = now,
            };
            var participants = await db
                .BingoParticipants.Where(value => value.CardId == card.Id)
                .OrderBy(value => value.TwitchUserId)
                .ToArrayAsync(ct);
            foreach (var participant in participants)
            {
                win.Recipients.Add(
                    new BingoWinRecipient
                    {
                        HostId = game.HostId,
                        TwitchUserId = participant.TwitchUserId,
                        Login = participant.Login,
                        DisplayName = participant.DisplayName,
                    }
                );
            }
            _ = db.BingoWins.Add(win);
            if (!await GrantPointRewardsAsync(db, win, now, ct))
            {
                return false;
            }
            AddDomainEvent(
                db,
                game,
                card,
                BingoDomainEventKind.WinCompleted,
                $"win:{card.PublicId:N}:{line.RuleKey}",
                $"{card.AssignmentName} completed {WinLabel(line)}",
                now
            );
            _ = existingRules.Add(line.RuleKey);
        }
        return true;
    }

    private async Task<bool> GrantPointRewardsAsync(
        BlokeBotDbContext db,
        BingoWin win,
        DateTime now,
        CancellationToken ct
    )
    {
        var amount = PointAmount.ParseAbsolute(win.PointsReward);
        if (amount.IsZero)
        {
            foreach (var recipient in win.Recipients)
            {
                recipient.PointsGranted = true;
            }
            return true;
        }
        foreach (var recipient in win.Recipients)
        {
            var login = NormalizeLogin(recipient.Login);
            var balance = await db.PointBalances.SingleOrDefaultAsync(
                value => value.HostId == win.HostId && value.Login == login,
                ct
            );
            if (balance is null)
            {
                balance = new PointBalance
                {
                    HostId = win.HostId,
                    Login = login,
                    Amount = "0",
                    UpdatedAtUtc = now,
                };
                _ = db.PointBalances.Add(balance);
            }
            var current = PointAmount.ParseAbsolute(balance.Amount);
            if (
                !await PointCreditCapacity.CanCreditAsync(
                    db,
                    win.HostId,
                    login,
                    current,
                    amount.Value,
                    ct
                )
            )
            {
                return false;
            }
            var next = current.Add(amount);
            balance.Amount = next.ToString();
            balance.UpdatedAtUtc = now;
            _ = db.PointLedgerEntries.Add(
                new PointLedgerEntry
                {
                    HostId = win.HostId,
                    CreatedAtUtc = now,
                    Kind = PointLedgerKind.BingoReward,
                    Login = login,
                    Delta = amount.ToString(),
                    BalanceAfter = next.ToString(),
                    Note = $"Bingo {win.RuleKey} reward",
                    OperationKey =
                        $"bingo:{win.PublicId:N}:{IdentityKey(recipient.TwitchUserId)}:points",
                }
            );
            recipient.PointsGranted = true;
        }
        return true;
    }

    private async Task GrantPendingAchievementsAsync(int hostId, CancellationToken ct)
    {
        await using var read = await dbFactory.CreateDbContextAsync(ct);
        var pending = await read
            .BingoWinRecipients.AsNoTracking()
            .Where(value => value.HostId == hostId && !value.AchievementGranted)
            .Join(
                read.BingoWins.AsNoTracking(),
                recipient => recipient.WinId,
                win => win.Id,
                (recipient, win) =>
                    new
                    {
                        RecipientId = recipient.Id,
                        recipient.TwitchUserId,
                        recipient.Login,
                        recipient.DisplayName,
                        win.PublicId,
                        win.AchievementKey,
                        win.CompletedAtUtc,
                    }
            )
            .Where(value => value.AchievementKey != null)
            .ToArrayAsync(ct);
        foreach (var value in pending)
        {
            var outcome = await achievementGrants.GrantAsync(
                new CommunityExternalGrantRequest(
                    hostId,
                    "bingo",
                    $"{value.PublicId:N}:{IdentityKey(value.TwitchUserId)}",
                    new(value.AchievementKey!),
                    new(value.TwitchUserId, value.Login, value.DisplayName),
                    value.CompletedAtUtc
                ),
                ct
            );
            if (outcome is not CommunityExternalGrantOutcome.Granted)
            {
                continue;
            }
            await using var update = await dbFactory.CreateDbContextAsync(ct);
            _ = await update
                .BingoWinRecipients.Where(recipient => recipient.Id == value.RecipientId)
                .ExecuteUpdateAsync(
                    properties =>
                        properties.SetProperty(recipient => recipient.AchievementGranted, true),
                    ct
                );
        }
        await using var complete = await dbFactory.CreateDbContextAsync(ct);
        var wins = await complete
            .BingoWins.Include(value => value.Recipients)
            .Where(value => value.HostId == hostId && value.RewardsCompletedAtUtc == null)
            .ToArrayAsync(ct);
        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var win in wins)
        {
            var achievementRequired = win.AchievementKey is not null;
            if (
                win.Recipients.All(value =>
                    value.PointsGranted && (!achievementRequired || value.AchievementGranted)
                )
            )
            {
                win.RewardsCompletedAtUtc = now;
            }
        }
        _ = await complete.SaveChangesAsync(ct);
    }

    private static BingoOperationOutcome.Invalid? Validate(BingoTemplateDraft draft)
    {
        if (draft.OperationId == Guid.Empty)
        {
            return new("An operation identity is required.");
        }
        if (string.IsNullOrWhiteSpace(draft.Name) || draft.Name.Trim().Length > 160)
        {
            return new("Template names are required and may contain at most 160 characters.");
        }
        if (draft.Squares.Count != draft.Dimension.SquareCount)
        {
            return new(
                $"A {draft.Dimension.Value}×{draft.Dimension.Value} template needs exactly {draft.Dimension.SquareCount} squares."
            );
        }
        if (draft.Squares.Select(value => value.Key).Distinct().Count() != draft.Squares.Count)
        {
            return new("Square keys must be unique within a revision.");
        }
        foreach (var square in draft.Squares)
        {
            if (string.IsNullOrWhiteSpace(square.Title) || square.Title.Trim().Length > 240)
            {
                return new("Every square needs a title of at most 240 characters.");
            }
            if (square.PrivateModeratorNote.Length > 2000)
            {
                return new("Private square notes may contain at most 2,000 characters.");
            }
            if (square is BingoSquareDefinition.IncomingRaid { MinimumViewerCount: < 1 })
            {
                return new("Incoming raid thresholds must be positive.");
            }
            if (square is BingoSquareDefinition.CounterReached { CounterId: < 1, Target: _ })
            {
                return new("Counter squares must reference a saved counter.");
            }
            if (square is BingoSquareDefinition.CounterReached { Target: < 1 })
            {
                return new("Counter targets must be positive.");
            }
        }
        return !draft.FullCardWinEnabled && draft.FullCardReward != BingoWinReward.None
            ? new("Enable full-card wins before configuring their reward.")
            : null;
    }

    private static BingoOperationOutcome.Invalid? Validate(BingoGameDraft draft)
    {
        if (draft.OperationId == Guid.Empty || draft.TemplateId.Value == Guid.Empty)
        {
            return new("A template and operation identity are required.");
        }
        if (string.IsNullOrWhiteSpace(draft.Seed) || draft.Seed.Trim().Length > 160)
        {
            return new("Record a seed of at most 160 characters.");
        }
        if (draft.ParticipantCap is < 1 || draft.TeamCap is < 1)
        {
            return new("Optional participant and team caps must be positive.");
        }
        var teamNames = draft.Teams.Select(NormalizeText).ToArray();
        if (draft.Mode == BingoGameMode.Team && teamNames.Length == 0)
        {
            return new("Team games need at least one host-defined team.");
        }
        if (draft.Mode != BingoGameMode.Team && teamNames.Length > 0)
        {
            return new("Only team games define teams.");
        }
        if (draft.TeamCap is { } teamCap && teamNames.Length > teamCap)
        {
            return new("The configured team count exceeds the team cap.");
        }
        var hasInvalidTeamName =
            teamNames.Any(string.IsNullOrWhiteSpace) || teamNames.Any(value => value.Length > 160);
        return hasInvalidTeamName
                ? new("Team names are required and may contain at most 160 characters.")
            : teamNames.Distinct(StringComparer.OrdinalIgnoreCase).Count() != teamNames.Length
                ? new("Team names must be unique.")
            : null;
    }

    private static async Task<bool> RewardDefinitionsExistAsync(
        BlokeBotDbContext db,
        int hostId,
        BingoTemplateDraft draft,
        CancellationToken ct
    )
    {
        var keys = new[]
        {
            draft.LineReward.AchievementKey?.Value,
            draft.FullCardReward.AchievementKey?.Value,
        }
            .Where(value => value is not null)
            .Cast<string>()
            .Distinct()
            .ToArray();
        if (keys.Length == 0)
        {
            return true;
        }
        var count = await db
            .CommunityDefinitions.AsNoTracking()
            .CountAsync(
                value =>
                    value.HostId == hostId
                    && keys.Contains(value.Key)
                    && value.Kind == CommunityDefinitionKind.Achievement
                    && value.Scope == CommunityProgressScope.Viewer
                    && value.EventRule == CommunityEventRuleKind.ExternalGrant,
                ct
            );
        return count == keys.Length;
    }

    private static async Task<bool> CounterDefinitionsExistAsync(
        BlokeBotDbContext db,
        int hostId,
        IReadOnlyList<BingoSquareDefinition> squares,
        CancellationToken ct
    )
    {
        var counterIds = squares
            .OfType<BingoSquareDefinition.CounterReached>()
            .Select(value => value.CounterId)
            .Distinct()
            .ToArray();
        return counterIds.Length == 0
            || await db
                .CustomCounters.AsNoTracking()
                .CountAsync(value => value.HostId == hostId && counterIds.Contains(value.Id), ct)
                == counterIds.Length;
    }

    private static BingoSquare ToEntity(int hostId, BingoSquareDefinition definition, int index)
    {
        (long? Threshold, string? Filter) values = definition switch
        {
            BingoSquareDefinition.IncomingRaid value => (value.MinimumViewerCount, null),
            BingoSquareDefinition.GuessingResult value => (
                null,
                NormalizeOptional(value.WinningAnswer)
            ),
            BingoSquareDefinition.StreamCategoryChanged value => (
                null,
                NormalizeOptional(value.CategoryId)
            ),
            BingoSquareDefinition.CounterReached value => (
                value.Target,
                value.CounterId.ToString(CultureInfo.InvariantCulture)
            ),
            _ => (null, null),
        };
        return new BingoSquare
        {
            HostId = hostId,
            Key = definition.Key.Value,
            SortOrder = index,
            Title = NormalizeText(definition.Title),
            Kind = definition.Kind,
            Threshold = values.Threshold,
            FilterToken = values.Filter,
            PrivateModeratorNote = definition.PrivateModeratorNote.Trim(),
        };
    }

    private static BingoSquareDefinition ToDefinition(BingoSquare value) =>
        value.Kind switch
        {
            BingoSquareKind.Manual => new BingoSquareDefinition.Manual(
                new(value.Key),
                value.Title,
                value.PrivateModeratorNote
            ),
            BingoSquareKind.IncomingRaid => new BingoSquareDefinition.IncomingRaid(
                new(value.Key),
                value.Title,
                checked((int)(value.Threshold ?? 1)),
                value.PrivateModeratorNote
            ),
            BingoSquareKind.BountyCompleted => new BingoSquareDefinition.BountyCompleted(
                new(value.Key),
                value.Title,
                value.PrivateModeratorNote
            ),
            BingoSquareKind.GuessingResult => new BingoSquareDefinition.GuessingResult(
                new(value.Key),
                value.Title,
                value.FilterToken,
                value.PrivateModeratorNote
            ),
            BingoSquareKind.GiveawayStarted => new BingoSquareDefinition.GiveawayStarted(
                new(value.Key),
                value.Title,
                value.PrivateModeratorNote
            ),
            BingoSquareKind.StreamCategoryChanged =>
                new BingoSquareDefinition.StreamCategoryChanged(
                    new(value.Key),
                    value.Title,
                    value.FilterToken,
                    value.PrivateModeratorNote
                ),
            BingoSquareKind.CounterReached => new BingoSquareDefinition.CounterReached(
                new(value.Key),
                value.Title,
                int.Parse(value.FilterToken!, CultureInfo.InvariantCulture),
                value.Threshold ?? 1,
                value.PrivateModeratorNote
            ),
            _ => throw new ArgumentOutOfRangeException(),
        };

    private static bool Matches(BingoSquare square, BingoAutomaticEvent sourceEvent) =>
        square.Kind == sourceEvent.Kind
        && square.Kind switch
        {
            BingoSquareKind.IncomingRaid => sourceEvent.Value >= square.Threshold,
            BingoSquareKind.GuessingResult or BingoSquareKind.StreamCategoryChanged =>
                square.FilterToken is null
                    || string.Equals(
                        square.FilterToken,
                        sourceEvent.FilterToken,
                        StringComparison.OrdinalIgnoreCase
                    ),
            BingoSquareKind.CounterReached => string.Equals(
                square.FilterToken,
                sourceEvent.FilterToken,
                StringComparison.Ordinal
            )
                && sourceEvent.Value >= square.Threshold,
            BingoSquareKind.BountyCompleted or BingoSquareKind.GiveawayStarted => true,
            _ => false,
        };

    private static BingoMark AddMark(
        BlokeBotDbContext db,
        BingoGame game,
        BingoCard card,
        string squareKey,
        int position,
        DateTime now
    )
    {
        var mark = new BingoMark
        {
            HostId = game.HostId,
            GameId = game.Id,
            CardId = card.Id,
            SquareKey = squareKey,
            Position = position,
            FirstMarkedAtUtc = now,
            ChangedAtUtc = now,
        };
        card.Marks.Add(mark);
        _ = db.BingoMarks.Add(mark);
        return mark;
    }

    private static BingoEvidence ToEvidence(
        BingoGame game,
        BingoCard card,
        BingoMark mark,
        BingoAutomaticEvent sourceEvent,
        DateTime recordedAt
    ) =>
        new()
        {
            HostId = game.HostId,
            GameId = game.Id,
            CardId = card.Id,
            Mark = mark,
            Action = BingoEvidenceAction.Marked,
            Source = BingoEvidenceSource.Automatic,
            EventKind = sourceEvent.Kind,
            Summary = sourceEvent.PublicSummary,
            ParticipantTwitchUserId = sourceEvent.Participant?.TwitchUserId,
            ParticipantLogin = sourceEvent.Participant?.Login,
            ParticipantDisplayName = sourceEvent.Participant?.DisplayName,
            OccurredAtUtc = sourceEvent.OccurredAtUtc.UtcDateTime,
            RecordedAtUtc = recordedAt,
        };

    private static BingoCard NewCard(
        int hostId,
        long gameId,
        string assignmentKey,
        string assignmentName,
        DateTime issuedAt
    ) => NewCard(hostId, gameId, Guid.NewGuid(), assignmentKey, assignmentName, issuedAt);

    private static BingoCard NewUniqueCard(
        int hostId,
        long gameId,
        string assignmentName,
        DateTime issuedAt
    )
    {
        var publicId = Guid.NewGuid();
        return NewCard(
            hostId,
            gameId,
            publicId,
            BingoCardAssignmentKey.Opaque(publicId),
            assignmentName,
            issuedAt
        );
    }

    private static BingoCard NewCard(
        int hostId,
        long gameId,
        Guid publicId,
        string assignmentKey,
        string assignmentName,
        DateTime issuedAt
    ) =>
        new()
        {
            HostId = hostId,
            GameId = gameId,
            PublicId = publicId,
            AssignmentKey = assignmentKey,
            AssignmentName = assignmentName,
            IssuedAtUtc = issuedAt,
        };

    private static IReadOnlyList<BingoCard> CreateCards(
        BingoGame game,
        int hostId,
        DateTime issuedAt
    )
    {
        switch (game.Mode)
        {
            case BingoGameMode.Shared:
            {
                var card = NewCard(hostId, game.Id, "shared", "Everyone", issuedAt);
                foreach (var participant in game.Participants)
                {
                    participant.Card = card;
                }
                return [card];
            }
            case BingoGameMode.UniquePerViewer:
                return game
                    .Participants.OrderBy(value => value.TwitchUserId, StringComparer.Ordinal)
                    .Select(participant =>
                    {
                        var card = NewUniqueCard(
                            hostId,
                            game.Id,
                            participant.DisplayName,
                            issuedAt
                        );
                        participant.Card = card;
                        return card;
                    })
                    .ToArray();
            case BingoGameMode.Team:
            {
                var cards = game
                    .Teams.Where(team => game.Participants.Any(value => value.TeamId == team.Id))
                    .OrderBy(value => value.SortOrder)
                    .ThenBy(value => value.Id)
                    .ToDictionary(
                        value => value.Id,
                        value =>
                            NewCard(
                                hostId,
                                game.Id,
                                $"team:{value.PublicId:N}",
                                value.Name,
                                issuedAt
                            )
                    );
                foreach (var participant in game.Participants)
                {
                    participant.Card = cards[participant.TeamId!.Value];
                }
                return cards.Values.ToArray();
            }
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private static IReadOnlyList<BingoSquareKey> Layout(BingoGame game, BingoCard card)
    {
        var squareKeys = game.TemplateRevision!.Squares.Select(value => value.Key).ToArray();
        return card.IssuedLayout is null
            ? BingoCardLayout.Generate(
                game.Seed,
                game.TemplateRevisionNumber,
                new(game.Dimension),
                card.AssignmentKey,
                squareKeys.Select(value => new BingoSquareKey(value))
            )
            : BingoIssuedLayout
                .Restore(card.IssuedLayout, game.Dimension, squareKeys)
                .Select(value => new BingoSquareKey(value))
                .ToArray();
    }

    private static int IndexOf(IReadOnlyList<BingoSquareKey> layout, string key)
    {
        for (var index = 0; index < layout.Count; index++)
        {
            if (layout[index].Value == key)
            {
                return index;
            }
        }
        throw new InvalidOperationException("Issued card does not contain its revision square.");
    }

    private static async Task<BingoGame?> LoadIssuedGameAsync(
        BlokeBotDbContext db,
        int hostId,
        CancellationToken ct
    ) =>
        await db
            .BingoGames.AsSplitQuery()
            .Include(value => value.TemplateRevision)
                .ThenInclude(value => value!.Squares)
            .Include(value => value.Teams)
            .Include(value => value.Participants)
            .Include(value => value.Cards)
                .ThenInclude(value => value.Marks)
            .Include(value => value.Cards)
                .ThenInclude(value => value.Wins)
            .SingleOrDefaultAsync(
                value => value.HostId == hostId && value.Status == BingoGameStatus.Issued,
                ct
            );

    private static async Task<IReadOnlyList<BingoGame>> LoadGamesAsync(
        BlokeBotDbContext db,
        int hostId,
        bool includeJoining,
        CancellationToken ct
    ) =>
        await db
            .BingoGames.AsNoTracking()
            .Where(value =>
                value.HostId == hostId
                && (includeJoining || value.Status != BingoGameStatus.Joining)
            )
            .OrderByDescending(value => value.CreatedAtUtc)
            .Take(50)
            .ToArrayAsync(ct);

    private static async Task<BingoGameView> ToViewAsync(
        BlokeBotDbContext db,
        BingoGame game,
        CancellationToken ct
    )
    {
        var loaded = await db
            .BingoGames.AsNoTracking()
            .AsSplitQuery()
            .Include(value => value.TemplateRevision)
                .ThenInclude(value => value!.Squares)
            .Include(value => value.Teams)
                .ThenInclude(value => value.Participants)
            .Include(value => value.Participants)
            .Include(value => value.Cards)
                .ThenInclude(value => value.Participants)
            .Include(value => value.Cards)
                .ThenInclude(value => value.Marks)
                    .ThenInclude(value => value.Evidence)
            .Include(value => value.Cards)
                .ThenInclude(value => value.Wins)
                    .ThenInclude(value => value.Recipients)
            .SingleAsync(value => value.Id == game.Id, ct);
        return ToView(loaded);
    }

    private static BingoGameView ToView(BingoGame game)
    {
        var definitions = game.TemplateRevision!.Squares.ToDictionary(value => value.Key);
        var cards = game
            .Cards.OrderBy(value => value.AssignmentName)
            .Select(card =>
            {
                var layout = Layout(game, card);
                var marks = card.Marks.ToDictionary(value => value.SquareKey);
                var squares = layout
                    .Select(
                        (key, position) =>
                        {
                            var definition = definitions[key.Value];
                            var mark = marks.GetValueOrDefault(key.Value);
                            return new BingoSquareView(
                                key,
                                position,
                                definition.Title,
                                definition.Kind,
                                mark?.IsActive == true,
                                mark?.Evidence.OrderBy(value => value.RecordedAtUtc)
                                    .Select(ToView)
                                    .ToArray()
                                    ?? []
                            );
                        }
                    )
                    .ToArray();
                return new BingoCardView(
                    new(card.PublicId),
                    card.AssignmentName,
                    card.Participants.Select(ToViewer).OrderBy(value => value.Login).ToArray(),
                    squares,
                    card.Wins.OrderBy(value => value.CompletedAtUtc).Select(ToView).ToArray()
                );
            })
            .ToArray();
        return new(
            new(game.PublicId),
            game.TemplateName,
            game.TemplateRevisionNumber,
            new(game.Dimension),
            game.Seed,
            game.Mode,
            game.Status,
            game.ParticipantCap,
            game.TeamCap,
            game.Participants.Select(ToViewer).OrderBy(value => value.Login).ToArray(),
            game.Teams.OrderBy(value => value.SortOrder)
                .Select(value => new BingoTeamView(
                    new(value.PublicId),
                    value.Name,
                    value.Participants.Select(ToViewer).OrderBy(member => member.Login).ToArray()
                ))
                .ToArray(),
            cards,
            game.CreatedAtUtc,
            game.IssuedAtUtc,
            game.CompletedAtUtc,
            game.ArchivedAtUtc
        );
    }

    private static BingoTemplateView ToView(
        BingoTemplate template,
        BingoTemplateRevision revision
    ) =>
        new(
            new(template.PublicId),
            template.Name,
            revision.Revision,
            new(revision.Dimension),
            revision.Squares.OrderBy(value => value.SortOrder).Select(ToDefinition).ToArray(),
            revision.FullCardWinEnabled,
            new(
                PointAmount.ParseAbsolute(revision.LinePointsReward),
                revision.LineAchievementKey is null
                    ? null
                    : new CommunityDefinitionKey(revision.LineAchievementKey)
            ),
            new(
                PointAmount.ParseAbsolute(revision.FullCardPointsReward),
                revision.FullCardAchievementKey is null
                    ? null
                    : new CommunityDefinitionKey(revision.FullCardAchievementKey)
            )
        );

    private static BingoEvidenceView ToView(BingoEvidence value) =>
        new(
            value.Action,
            value.Source,
            value.EventKind,
            value.Summary,
            value.ParticipantTwitchUserId is null
                ? null
                : new(
                    value.ParticipantTwitchUserId,
                    value.ParticipantLogin!,
                    value.ParticipantDisplayName!
                ),
            value.OccurredAtUtc,
            value.RecordedAtUtc
        );

    private static BingoWinView ToView(BingoWin value) =>
        new(
            value.PublicId,
            value.Kind,
            value.RuleIndex,
            value.RuleKey,
            value.CompletedAtUtc,
            value.RewardsCompletedAtUtc is not null,
            value
                .Recipients.Select(recipient => new BingoViewer(
                    recipient.TwitchUserId,
                    recipient.Login,
                    recipient.DisplayName
                ))
                .ToArray()
        );

    private static BingoViewer ToViewer(BingoParticipant value) =>
        new(value.TwitchUserId, value.Login, value.DisplayName);

    private static async Task<bool> FeatureEnabledAsync(
        BlokeBotDbContext db,
        int hostId,
        CancellationToken ct
    ) =>
        await db
            .Hosts.AsNoTracking()
            .AnyAsync(
                value =>
                    value.Id == hostId
                    && (value.EnabledFeatures & HostFeatureFlags.Bingo) == HostFeatureFlags.Bingo,
                ct
            );

    private static async Task<bool> OperationRecordedAsync(
        BlokeBotDbContext db,
        int hostId,
        Guid operationId,
        CancellationToken ct
    ) =>
        await db
            .BingoModerationAudit.AsNoTracking()
            .AnyAsync(value => value.HostId == hostId && value.OperationId == operationId, ct);

    private static void AddAudit(
        BlokeBotDbContext db,
        int hostId,
        long gameId,
        long? cardId,
        long? markId,
        Guid operationId,
        string action,
        BingoActor actor,
        string privateNote,
        DateTime occurredAt
    ) =>
        _ = db.BingoModerationAudit.Add(
            new BingoModerationAudit
            {
                HostId = hostId,
                GameId = gameId,
                CardId = cardId,
                MarkId = markId,
                OperationId = operationId,
                Action = action,
                ActorTwitchUserId = NormalizeIdentity(actor.TwitchUserId),
                ActorLogin = NormalizeLogin(actor.Login),
                PrivateNote = privateNote.Trim(),
                OccurredAtUtc = occurredAt,
            }
        );

    private static void AddDomainEvent(
        BlokeBotDbContext db,
        BingoGame game,
        BingoCard? card,
        BingoDomainEventKind kind,
        string operationKey,
        string publicSummary,
        DateTime occurredAt
    ) =>
        _ = db.BingoEvents.Add(
            new BingoDomainEvent
            {
                HostId = game.HostId,
                GameId = game.Id,
                CardId = card?.Id,
                Kind = kind,
                OperationKey = operationKey,
                PublicPayload = JsonSerializer.Serialize(
                    new
                    {
                        game = game.PublicId,
                        card = card?.PublicId,
                        summary = publicSummary,
                        occurredAt,
                    }
                ),
                OccurredAtUtc = occurredAt,
            }
        );

    private async Task PublishAndOverlayAsync(BingoOverlayEvent value, CancellationToken ct)
    {
        await PublishChangedAsync(ct);
        foreach (var observer in _overlayObservers)
        {
            await observer.BingoEventAsync(value, ct);
        }
    }

    private async Task PublishChangedAsync(CancellationToken ct) =>
        _ = await events.PublishAsync(AppEventKind.BingoChanged, ct);

    private async Task<T> LockedAsync<T>(int hostId, Func<Task<T>> operation, CancellationToken ct)
    {
        var gate = _hostGates.GetOrAdd(hostId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            return await operation();
        }
        finally
        {
            _ = gate.Release();
        }
    }

    private Task<T> LockedLifecycleAsync<T>(
        int hostId,
        Func<Task<T>> operation,
        CancellationToken ct
    ) => LockedAsync(hostId, () => RetryLifecyclePersistenceAsync(operation, ct), ct);

    private static async Task<T> RetryLifecyclePersistenceAsync<T>(
        Func<Task<T>> operation,
        CancellationToken ct
    )
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await operation();
            }
            catch (Exception exception)
                when (attempt < _persistenceRetryCount && IsLifecyclePersistenceCollision(exception)
                )
            {
                await Task.Delay(TimeSpan.FromMilliseconds(attempt * 5), ct);
            }
        }
    }

    private static bool IsLifecyclePersistenceCollision(Exception exception) =>
        exception switch
        {
            DbUpdateConcurrencyException => true,
            SqliteException
            {
                SqliteErrorCode: SQLitePCL.raw.SQLITE_BUSY or SQLitePCL.raw.SQLITE_LOCKED,
            } => true,
            SqliteException
            {
                SqliteErrorCode: SQLitePCL.raw.SQLITE_CONSTRAINT,
                SqliteExtendedErrorCode: SQLitePCL.raw.SQLITE_CONSTRAINT_UNIQUE,
            } => true,
            DbUpdateException { InnerException: { } inner } => IsLifecyclePersistenceCollision(
                inner
            ),
            _ => false,
        };

    private static string WinLabel(BingoWinLine line) =>
        line.Kind switch
        {
            BingoWinKind.Row => $"row {line.Index + 1}",
            BingoWinKind.Column => $"column {line.Index + 1}",
            BingoWinKind.Diagonal => "a diagonal",
            BingoWinKind.FullCard => "the full card",
            _ => throw new ArgumentOutOfRangeException(),
        };

    private static string NormalizeIdentity(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value.Trim();
    }

    private static string IdentityKey(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string NormalizeLogin(string value) => LoginName.Parse(value).Value;

    private static string NormalizeDisplayName(BingoViewer value) =>
        string.IsNullOrWhiteSpace(value.DisplayName)
            ? NormalizeLogin(value.Login)
            : value.DisplayName.Trim();

    private static string NormalizeText(string value) => value.Trim();

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();

    private enum RosterChange
    {
        Join,
        Move,
        Remove,
    }
}
