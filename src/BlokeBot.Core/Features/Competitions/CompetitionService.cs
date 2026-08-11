using System.Text.Json;
using BlokeBot.Core.Features.CommunityProgression;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Eventing;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Competitions;

public sealed class CompetitionService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    EventBus<AppEventKind> events,
    ICommunityAchievementGrantService achievements,
    IEnumerable<ICompetitionLifecycleObserver> observers,
    TimeProvider timeProvider
)
{
    private static readonly HostFeatureFlags _requiredFeature = HostFeatureFlags.Competitions;

    public async Task<IReadOnlyList<CompetitionModeratorView>> GetModeratorAsync(
        int hostId,
        CancellationToken ct
    )
    {
        if (!await FeatureEnabledAsync(hostId, ct))
        {
            return [];
        }
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var host = await db.Hosts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == hostId, ct);
        if (host is null)
        {
            return [];
        }
        var competitions = await Query(db)
            .Where(x => x.HostId == hostId)
            .OrderByDescending(x => x.UpdatedAtUtc)
            .ToArrayAsync(ct);
        return competitions.Select(x => ToModerator(x, host.Login)).ToArray();
    }

    public async Task<CompetitionPublicBoard?> GetPublicAsync(
        string hostLogin,
        CancellationToken ct
    )
    {
        var login = CommunityInput.NormalizeLogin(hostLogin);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var host = await db
            .Hosts.AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.Login == login && (x.EnabledFeatures & _requiredFeature) == _requiredFeature,
                ct
            );
        if (host is null)
        {
            return null;
        }
        var competitions = await Query(db)
            .Where(x => x.HostId == host.Id && x.Status != CompetitionStatus.Draft)
            .OrderByDescending(x => x.UpdatedAtUtc)
            .ToArrayAsync(ct);
        return new(
            host.Login,
            competitions
                .Where(x => x.Status != CompetitionStatus.Archived)
                .Select(x => ToView(x, host.Login))
                .ToArray(),
            competitions
                .Where(x => x.Status == CompetitionStatus.Archived)
                .Select(x => ToView(x, host.Login))
                .ToArray()
        );
    }

    public async Task<CompetitionOutcome> CreateAsync(
        int hostId,
        CompetitionDraft draft,
        CancellationToken ct
    )
    {
        if (Validate(draft) is { } invalid)
        {
            return invalid;
        }
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await FeatureEnabledAsync(db, hostId, ct))
        {
            return new CompetitionOutcome.FeatureDisabled();
        }
        if (
            await db.Competitions.AnyAsync(
                x => x.HostId == hostId && x.CreationOperationId == draft.OperationId,
                ct
            )
        )
        {
            return new CompetitionOutcome.Succeeded(true);
        }
        var achievementKeys = new[]
        {
            NormalizeKey(draft.WinnerAchievementKey),
            NormalizeKey(draft.RunnerUpAchievementKey),
        }
            .Concat(draft.MilestoneRewards.Select(x => NormalizeKey(x.AchievementKey)))
            .Where(key => key.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (
            achievementKeys.Length > 0
            && await db.CommunityDefinitions.CountAsync(
                value =>
                    value.HostId == hostId
                    && achievementKeys.Contains(value.Key)
                    && value.Kind == CommunityDefinitionKind.Achievement
                    && value.Scope == CommunityProgressScope.Viewer
                    && value.EventRule == CommunityEventRuleKind.ExternalGrant,
                ct
            ) != achievementKeys.Length
        )
        {
            return new CompetitionOutcome.Invalid(
                "Competition rewards must reference predeclared viewer achievements that accept external grants."
            );
        }
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var competition = new Competition
        {
            HostId = hostId,
            PublicId = Guid.NewGuid(),
            CreationOperationId = draft.OperationId,
            Name = draft.Name.Trim(),
            Description = draft.Description.Trim(),
            Format = draft.Format,
            EntryKind = draft.EntryKind,
            Status = CompetitionStatus.Draft,
            Seeding = draft.Seeding,
            Tiebreak = draft.Tiebreak,
            Capacity = draft.Capacity,
            TeamSize = draft.EntryKind == CompetitionEntryKind.Individual ? 1 : draft.TeamSize,
            MinimumPoints = draft.MinimumPoints.ToString(),
            WinPoints = draft.WinPoints,
            DrawPoints = draft.DrawPoints,
            LossPoints = draft.LossPoints,
            Seed = draft.Seed.Trim(),
            AlgorithmVersion = CompetitionSchedule.AlgorithmVersion,
            ReminderHoursBefore = draft.ReminderHoursBefore,
            ReminderMessage = draft.ReminderMessage.Trim(),
            WinnerPoints = draft.WinnerPoints.ToString(),
            RunnerUpPoints = draft.RunnerUpPoints.ToString(),
            WinnerAchievementKey = NormalizeKey(draft.WinnerAchievementKey),
            RunnerUpAchievementKey = NormalizeKey(draft.RunnerUpAchievementKey),
            PrivateLobbyInformation = draft.PrivateLobbyInformation.Trim(),
            Revision = 1,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        competition.MilestoneRewards.AddRange(
            draft.MilestoneRewards.Select(rule => new CompetitionMilestoneRewardRule
            {
                HostId = hostId,
                WinsRequired = rule.WinsRequired,
                Points = rule.Points.ToString(),
                AchievementKey = NormalizeKey(rule.AchievementKey),
            })
        );
        AddAudit(
            competition,
            draft.OperationId,
            CompetitionAuditAction.Created,
            draft.Actor,
            draft.PrivateReason,
            now
        );
        var lifecycle = AddEvent(
            competition,
            draft.OperationId,
            CompetitionEventKind.Created,
            new { competition.Name, Format = competition.Format.ToString() },
            now
        );
        _ = db.Competitions.Add(competition);
        _ = await db.SaveChangesAsync(ct);
        await PublishAsync(lifecycle, ct);
        return new CompetitionOutcome.Succeeded();
    }

    public Task<CompetitionOutcome> OpenRegistrationAsync(
        int hostId,
        CompetitionTransition command,
        CancellationToken ct
    ) =>
        TransitionAsync(
            hostId,
            command,
            CompetitionStatus.Draft,
            CompetitionStatus.Registration,
            CompetitionAuditAction.RegistrationOpened,
            CompetitionEventKind.RegistrationOpened,
            ct
        );

    public async Task<CompetitionOutcome> RegisterAsync(
        int hostId,
        CompetitionRegistration registration,
        CancellationToken ct
    )
    {
        if (Validate(registration) is { } invalid)
        {
            return invalid;
        }
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await FeatureEnabledAsync(db, hostId, ct))
        {
            return new CompetitionOutcome.FeatureDisabled();
        }
        var competition = await db
            .Competitions.Include(x => x.Entrants)
                .ThenInclude(x => x.Members)
            .SingleOrDefaultAsync(
                x => x.HostId == hostId && x.PublicId == registration.CompetitionId.Value,
                ct
            );
        if (competition is null)
        {
            return new CompetitionOutcome.NotFound();
        }
        if (competition.Entrants.Any(x => x.RegistrationOperationId == registration.OperationId))
        {
            return new CompetitionOutcome.Succeeded(true);
        }
        if (competition.Status != CompetitionStatus.Registration)
        {
            return new CompetitionOutcome.Conflict("Registration is not open.");
        }
        if (competition.Entrants.Count >= competition.Capacity)
        {
            return new CompetitionOutcome.Conflict("Competition capacity has been reached.");
        }
        if (registration.Members.Count != competition.TeamSize)
        {
            return new CompetitionOutcome.Invalid(
                $"Registration requires exactly {competition.TeamSize} member(s)."
            );
        }
        var logins = registration
            .Members.Select(x => CommunityInput.NormalizeLogin(x.Login))
            .ToArray();
        if (logins.Distinct(StringComparer.Ordinal).Count() != logins.Length)
        {
            return new CompetitionOutcome.Invalid(
                "A viewer cannot occupy more than one team slot."
            );
        }
        if (competition.Entrants.SelectMany(x => x.Members).Any(x => logins.Contains(x.Login)))
        {
            return new CompetitionOutcome.Conflict(
                "A viewer is already registered in this competition."
            );
        }
        var minimum = PointAmount.ParseAbsolute(competition.MinimumPoints);
        if (!minimum.IsZero)
        {
            var balances = await db
                .PointBalances.AsNoTracking()
                .Where(x => x.HostId == hostId && logins.Contains(x.Login))
                .ToDictionaryAsync(x => x.Login, x => x.Amount, ct);
            if (
                logins.Any(login =>
                    !balances.TryGetValue(login, out var amount)
                    || PointAmount.ParseAbsolute(amount) < minimum
                )
            )
            {
                return new CompetitionOutcome.Conflict(
                    $"Every member needs at least {minimum.ToDisplayString()} points to enter."
                );
            }
        }
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var entrant = new CompetitionEntrant
        {
            HostId = hostId,
            PublicId = Guid.NewGuid(),
            RegistrationOperationId = registration.OperationId,
            Name = registration.Name.Trim(),
            SeedRank = registration.SeedRank,
            RegisteredAtUtc = now,
            Members = registration
                .Members.Select(
                    (member, _) =>
                        new CompetitionEntrantMember
                        {
                            HostId = hostId,
                            TwitchUserId = member.TwitchUserId.Trim(),
                            Login = CommunityInput.NormalizeLogin(member.Login),
                            DisplayName = member.DisplayName.Trim(),
                            PrivateContact = member.PrivateContact.Trim(),
                        }
                )
                .ToList(),
        };
        competition.Entrants.Add(entrant);
        competition.Revision++;
        competition.UpdatedAtUtc = now;
        AddAudit(
            competition,
            registration.OperationId,
            CompetitionAuditAction.EntrantRegistered,
            registration.Actor,
            registration.PrivateReason,
            now
        );
        var lifecycle = AddEvent(
            competition,
            registration.OperationId,
            CompetitionEventKind.EntrantRegistered,
            new { Entrant = entrant.Name, Count = competition.Entrants.Count },
            now
        );
        _ = await db.SaveChangesAsync(ct);
        await PublishAsync(lifecycle, ct);
        return new CompetitionOutcome.Succeeded();
    }

    public async Task<CompetitionOutcome> StartAsync(
        int hostId,
        CompetitionTransition command,
        DateTime? firstRoundAtUtc,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await FeatureEnabledAsync(db, hostId, ct))
        {
            return new CompetitionOutcome.FeatureDisabled();
        }
        var competition = await db
            .Competitions.Include(x => x.Entrants)
                .ThenInclude(x => x.Members)
            .Include(x => x.Matches)
            .Include(x => x.Audits)
            .SingleOrDefaultAsync(
                x => x.HostId == hostId && x.PublicId == command.CompetitionId.Value,
                ct
            );
        if (competition is null)
        {
            return new CompetitionOutcome.NotFound();
        }
        if (competition.Audits.Any(x => x.OperationId == command.OperationId))
        {
            return new CompetitionOutcome.Succeeded(true);
        }
        if (competition.Revision != command.ExpectedRevision)
        {
            return new CompetitionOutcome.Conflict(
                "Competition changed; reload before starting it."
            );
        }
        if (competition.Status != CompetitionStatus.Registration || competition.Entrants.Count < 2)
        {
            return new CompetitionOutcome.Conflict(
                "Open registration and add at least two entrants before starting."
            );
        }
        var orderedEntrants = competition
            .Entrants.OrderBy(x => x.SeedRank ?? int.MaxValue)
            .ThenBy(x => x.RegisteredAtUtc)
            .ThenBy(x => x.Id)
            .ToArray();
        var order = CompetitionSchedule.Order(
            orderedEntrants.Length,
            competition.Seeding,
            competition.Seed
        );
        var schedule =
            competition.Format == CompetitionFormat.Tournament
                ? CompetitionSchedule.GenerateTournament(order)
                : CompetitionSchedule.GenerateLeague(order);
        var first = firstRoundAtUtc ?? timeProvider.GetUtcNow().UtcDateTime;
        foreach (var slot in schedule)
        {
            var scheduled = first.AddDays(slot.Round - 1);
            competition.Matches.Add(
                new CompetitionMatch
                {
                    HostId = hostId,
                    PublicId = Guid.NewGuid(),
                    Round = slot.Round,
                    Position = slot.Position,
                    EntrantA = slot.EntrantA is { } a ? orderedEntrants[a] : null,
                    EntrantB = slot.EntrantB is { } b ? orderedEntrants[b] : null,
                    EntrantAId = slot.EntrantA is { } entrantA
                        ? orderedEntrants[entrantA].Id
                        : null,
                    EntrantBId = slot.EntrantB is { } entrantB
                        ? orderedEntrants[entrantB].Id
                        : null,
                    Status = CompetitionMatchStatus.Pending,
                    ScheduledAtUtc = scheduled,
                    ReminderDueAtUtc =
                        slot.EntrantA is not null
                        && slot.EntrantB is not null
                        && competition.ReminderHoursBefore > 0
                            ? scheduled.AddHours(-competition.ReminderHoursBefore)
                            : null,
                }
            );
        }
        if (competition.Format == CompetitionFormat.Tournament)
        {
            RecomputeTournament(
                competition,
                null,
                command.Actor,
                command.PrivateReason,
                command.OperationId,
                timeProvider.GetUtcNow().UtcDateTime
            );
        }
        var now = timeProvider.GetUtcNow().UtcDateTime;
        competition.Status = CompetitionStatus.Running;
        competition.StartedAtUtc = now;
        competition.UpdatedAtUtc = now;
        competition.Revision++;
        AddAudit(
            competition,
            command.OperationId,
            CompetitionAuditAction.Started,
            command.Actor,
            command.PrivateReason,
            now
        );
        var lifecycle = AddEvent(
            competition,
            command.OperationId,
            CompetitionEventKind.Started,
            new
            {
                competition.Name,
                Entrants = competition.Entrants.Count,
                competition.Seed,
                competition.AlgorithmVersion,
            },
            now
        );
        _ = await db.SaveChangesAsync(ct);
        await PublishAsync(lifecycle, ct);
        return new CompetitionOutcome.Succeeded();
    }

    public async Task<CompetitionOutcome> ConfirmResultAsync(
        int hostId,
        CompetitionResultCommand command,
        CancellationToken ct
    )
    {
        if (command.ScoreA < 0 || command.ScoreB < 0)
        {
            return new CompetitionOutcome.Invalid("Scores cannot be negative.");
        }
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await FeatureEnabledAsync(db, hostId, ct))
        {
            return new CompetitionOutcome.FeatureDisabled();
        }
        var competition = await db
            .Competitions.Include(x => x.Entrants)
                .ThenInclude(x => x.Members)
            .Include(x => x.Matches)
            .Include(x => x.Audits)
            .SingleOrDefaultAsync(
                x => x.HostId == hostId && x.PublicId == command.CompetitionId.Value,
                ct
            );
        if (competition is null)
        {
            return new CompetitionOutcome.NotFound();
        }
        if (competition.Audits.Any(x => x.OperationId == command.OperationId))
        {
            return new CompetitionOutcome.Succeeded(true);
        }
        if (
            competition.Status != CompetitionStatus.Running
            || competition.Revision != command.ExpectedRevision
        )
        {
            return new CompetitionOutcome.Conflict(
                "Competition changed or is not running; reload before entering a result."
            );
        }
        var match = competition.Matches.SingleOrDefault(x => x.PublicId == command.MatchId.Value);
        if (match?.EntrantAId is null || match.EntrantBId is null)
        {
            return new CompetitionOutcome.Conflict(
                "Both entrants must be known before confirming this match."
            );
        }
        if (competition.Format == CompetitionFormat.Tournament && command.ScoreA == command.ScoreB)
        {
            return new CompetitionOutcome.Invalid("Tournament matches cannot end in a draw.");
        }
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var previousA = match.ScoreA;
        var previousB = match.ScoreB;
        var previousWinner = match.WinnerEntrantId;
        match.ScoreA = command.ScoreA;
        match.ScoreB = command.ScoreB;
        match.WinnerEntrantId =
            command.ScoreA == command.ScoreB ? null
            : command.ScoreA > command.ScoreB ? match.EntrantAId
            : match.EntrantBId;
        match.Status = CompetitionMatchStatus.Confirmed;
        match.ConfirmedAtUtc = now;
        var corrected = previousA is not null;
        competition.Audits.Add(
            new CompetitionAudit
            {
                HostId = hostId,
                OperationId = command.OperationId,
                MatchId = match.Id,
                Action = corrected
                    ? CompetitionAuditAction.ResultCorrected
                    : CompetitionAuditAction.ResultConfirmed,
                ActorTwitchUserId = command.Actor.TwitchUserId,
                ActorLogin = CommunityInput.NormalizeLogin(command.Actor.Login),
                PrivateReason = command.PrivateReason.Trim(),
                PreviousScoreA = previousA,
                PreviousScoreB = previousB,
                PreviousWinnerEntrantId = previousWinner,
                NewScoreA = command.ScoreA,
                NewScoreB = command.ScoreB,
                NewWinnerEntrantId = match.WinnerEntrantId,
                OccurredAtUtc = now,
            }
        );
        if (competition.Format == CompetitionFormat.Tournament)
        {
            RecomputeTournament(
                competition,
                match,
                command.Actor,
                command.PrivateReason,
                command.OperationId,
                now
            );
        }
        competition.Revision++;
        competition.UpdatedAtUtc = now;
        var kind = corrected
            ? CompetitionEventKind.ResultCorrected
            : CompetitionEventKind.ResultConfirmed;
        var lifecycle = AddEvent(
            competition,
            command.OperationId,
            kind,
            new
            {
                Match = match.PublicId,
                match.Round,
                match.Position,
                ScoreA = command.ScoreA,
                ScoreB = command.ScoreB,
            },
            now
        );
        _ = await db.SaveChangesAsync(ct);
        await PublishAsync(lifecycle, ct);
        return new CompetitionOutcome.Succeeded();
    }

    public async Task<CompetitionOutcome> CompleteAsync(
        int hostId,
        CompetitionTransition command,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await FeatureEnabledAsync(db, hostId, ct))
        {
            return new CompetitionOutcome.FeatureDisabled();
        }
        var competition = await Query(db)
            .SingleOrDefaultAsync(
                x => x.HostId == hostId && x.PublicId == command.CompetitionId.Value,
                ct
            );
        if (competition is null)
        {
            return new CompetitionOutcome.NotFound();
        }
        if (
            competition.Status == CompetitionStatus.Completed
            && competition.Audits.Any(x => x.OperationId == command.OperationId)
        )
        {
            await ReconcileAchievementsAsync(competition, ct);
            return new CompetitionOutcome.Succeeded(true);
        }
        if (
            competition.Status != CompetitionStatus.Running
            || competition.Revision != command.ExpectedRevision
        )
        {
            return new CompetitionOutcome.Conflict(
                "Competition changed or is not running; reload before completing it."
            );
        }
        if (!CanComplete(competition))
        {
            return new CompetitionOutcome.Conflict(
                "Every deciding match must have a confirmed result before completion."
            );
        }
        var placements = Placement(competition);
        var standings = Standings(competition);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        competition.Status = CompetitionStatus.Completed;
        competition.CompletedAtUtc = now;
        competition.UpdatedAtUtc = now;
        competition.Revision++;
        AddAudit(
            competition,
            command.OperationId,
            CompetitionAuditAction.Completed,
            command.Actor,
            command.PrivateReason,
            now
        );
        await GrantPlacementAsync(
            db,
            competition,
            placements[0],
            1,
            PointAmount.ParseAbsolute(competition.WinnerPoints),
            competition.WinnerAchievementKey,
            now,
            ct
        );
        if (placements.Count > 1)
        {
            await GrantPlacementAsync(
                db,
                competition,
                placements[1],
                2,
                PointAmount.ParseAbsolute(competition.RunnerUpPoints),
                competition.RunnerUpAchievementKey,
                now,
                ct
            );
        }
        foreach (var rule in competition.MilestoneRewards.OrderBy(x => x.WinsRequired))
        {
            foreach (var standing in standings.Where(x => x.Wins >= rule.WinsRequired))
            {
                await GrantWinMilestoneAsync(
                    db,
                    competition,
                    competition.Entrants.Single(x => x.Id == standing.EntrantId),
                    rule,
                    now,
                    ct
                );
            }
        }
        var milestoneRecipients = competition.Rewards.Count(x =>
            x.Kind == CompetitionRewardKind.WinMilestone
        );
        var lifecycle = AddEvent(
            competition,
            command.OperationId,
            CompetitionEventKind.Completed,
            new
            {
                competition.Name,
                Winner = placements[0].Name,
                MilestoneRecipients = milestoneRecipients,
            },
            now
        );
        var rewardsLifecycle = AddEvent(
            competition,
            Guid.NewGuid(),
            CompetitionEventKind.RewardsGranted,
            new
            {
                competition.Name,
                Recipients = competition.Rewards.Count,
                MilestoneRecipients = milestoneRecipients,
            },
            now
        );
        _ = await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await ReconcileAchievementsAsync(competition, ct);
        await PublishAsync(
            lifecycle,
            ct,
            pointsChanged: competition.Rewards.Any(x =>
                PointAmount.ParseAbsolute(x.PointsGranted) > PointAmount.Zero
            )
        );
        await PublishAsync(rewardsLifecycle, ct);
        return new CompetitionOutcome.Succeeded();
    }

    public Task<CompetitionOutcome> ArchiveAsync(
        int hostId,
        CompetitionTransition command,
        CancellationToken ct
    ) =>
        TransitionAsync(
            hostId,
            command,
            CompetitionStatus.Completed,
            CompetitionStatus.Archived,
            CompetitionAuditAction.Archived,
            CompetitionEventKind.Archived,
            ct
        );

    public async Task<int> SuppressDueRemindersAsync(int hostId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var host = await db.Hosts.SingleOrDefaultAsync(x => x.Id == hostId, ct);
        if (host is null || host.EnabledFeatures.Contains(_requiredFeature))
        {
            return 0;
        }
        var now = timeProvider.GetUtcNow().UtcDateTime;
        return await db
            .CompetitionMatches.Where(x =>
                x.HostId == hostId
                && x.ReminderDueAtUtc <= now
                && x.ReminderDeliveredAtUtc == null
                && x.ReminderSuppressedAtUtc == null
            )
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(x => x.ReminderSuppressedAtUtc, now),
                ct
            );
    }

    private async Task<CompetitionOutcome> TransitionAsync(
        int hostId,
        CompetitionTransition command,
        CompetitionStatus expected,
        CompetitionStatus target,
        CompetitionAuditAction action,
        CompetitionEventKind eventKind,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await FeatureEnabledAsync(db, hostId, ct))
        {
            return new CompetitionOutcome.FeatureDisabled();
        }
        var competition = await db
            .Competitions.Include(x => x.Audits)
            .SingleOrDefaultAsync(
                x => x.HostId == hostId && x.PublicId == command.CompetitionId.Value,
                ct
            );
        if (competition is null)
        {
            return new CompetitionOutcome.NotFound();
        }
        if (competition.Audits.Any(x => x.OperationId == command.OperationId))
        {
            return new CompetitionOutcome.Succeeded(true);
        }
        if (competition.Status != expected || competition.Revision != command.ExpectedRevision)
        {
            return new CompetitionOutcome.Conflict(
                $"Competition must be {expected} at the expected revision."
            );
        }
        var now = timeProvider.GetUtcNow().UtcDateTime;
        competition.Status = target;
        competition.Revision++;
        competition.UpdatedAtUtc = now;
        if (target == CompetitionStatus.Registration)
        {
            competition.RegistrationOpenedAtUtc = now;
        }
        if (target == CompetitionStatus.Archived)
        {
            competition.ArchivedAtUtc = now;
        }
        AddAudit(
            competition,
            command.OperationId,
            action,
            command.Actor,
            command.PrivateReason,
            now
        );
        var lifecycle = AddEvent(
            competition,
            command.OperationId,
            eventKind,
            new { competition.Name, Status = target.ToString() },
            now
        );
        _ = await db.SaveChangesAsync(ct);
        await PublishAsync(lifecycle, ct);
        return new CompetitionOutcome.Succeeded();
    }

    private static void RecomputeTournament(
        Competition competition,
        CompetitionMatch? changed,
        CompetitionActor actor,
        string reason,
        Guid operationId,
        DateTime now
    )
    {
        var rounds = competition.Matches.Max(x => x.Round);
        for (var round = 2; round <= rounds; round++)
        {
            foreach (
                var match in competition
                    .Matches.Where(x => x.Round == round)
                    .OrderBy(x => x.Position)
            )
            {
                var previousA = competition.Matches.Single(x =>
                    x.Round == round - 1 && x.Position == match.Position * 2
                );
                var previousB = competition.Matches.Single(x =>
                    x.Round == round - 1 && x.Position == (match.Position * 2) + 1
                );
                var entrantA = Advanced(previousA);
                var entrantB = Advanced(previousB);
                if (match.EntrantAId == entrantA && match.EntrantBId == entrantB)
                {
                    continue;
                }
                if (match.Status == CompetitionMatchStatus.Confirmed)
                {
                    competition.Audits.Add(
                        new CompetitionAudit
                        {
                            HostId = competition.HostId,
                            OperationId = StableDerivedOperation(operationId, match.PublicId),
                            MatchId = match.Id,
                            Action = CompetitionAuditAction.DownstreamReset,
                            ActorTwitchUserId = actor.TwitchUserId,
                            ActorLogin = CommunityInput.NormalizeLogin(actor.Login),
                            PrivateReason = reason.Trim(),
                            PreviousScoreA = match.ScoreA,
                            PreviousScoreB = match.ScoreB,
                            PreviousWinnerEntrantId = match.WinnerEntrantId,
                            OccurredAtUtc = now,
                        }
                    );
                }
                match.EntrantAId = entrantA;
                match.EntrantBId = entrantB;
                match.ScoreA = null;
                match.ScoreB = null;
                match.WinnerEntrantId = null;
                match.Status = CompetitionMatchStatus.Pending;
                match.ConfirmedAtUtc = null;
                match.ReminderDeliveredAtUtc = null;
                match.ReminderSuppressedAtUtc = null;
                match.ReminderDueAtUtc =
                    entrantA is not null
                    && entrantB is not null
                    && match.ScheduledAtUtc is { } scheduled
                    && competition.ReminderHoursBefore > 0
                        ? Max(scheduled.AddHours(-competition.ReminderHoursBefore), now)
                        : null;
            }
        }
    }

    private static long? Advanced(CompetitionMatch match) =>
        match.Status == CompetitionMatchStatus.Confirmed ? match.WinnerEntrantId
        : match.EntrantAId is not null && match.EntrantBId is null ? match.EntrantAId
        : match.EntrantBId is not null && match.EntrantAId is null ? match.EntrantBId
        : null;

    private static Guid StableDerivedOperation(Guid operationId, Guid matchId)
    {
        Span<byte> bytes = stackalloc byte[16];
        _ = operationId.TryWriteBytes(bytes);
        Span<byte> match = stackalloc byte[16];
        _ = matchId.TryWriteBytes(match);
        for (var index = 0; index < bytes.Length; index++)
        {
            bytes[index] ^= match[index];
        }
        return new Guid(bytes);
    }

    private static DateTime Max(DateTime left, DateTime right) => left >= right ? left : right;

    private static bool CanComplete(Competition competition) =>
        competition.Format == CompetitionFormat.Tournament
            ? competition.Matches.OrderByDescending(x => x.Round).First().Status
                == CompetitionMatchStatus.Confirmed
            : competition.Matches.All(x => x.Status == CompetitionMatchStatus.Confirmed);

    private static IReadOnlyList<CompetitionEntrant> Placement(Competition competition)
    {
        if (competition.Format == CompetitionFormat.Tournament)
        {
            var final = competition.Matches.OrderByDescending(x => x.Round).First();
            var winner = competition.Entrants.Single(x => x.Id == final.WinnerEntrantId);
            var runnerUpId = final.EntrantAId == winner.Id ? final.EntrantBId : final.EntrantAId;
            return [winner, competition.Entrants.Single(x => x.Id == runnerUpId)];
        }
        var standings = Standings(competition);
        return standings
            .Select(row => competition.Entrants.Single(x => x.Id == row.EntrantId))
            .ToArray();
    }

    private static Task GrantPlacementAsync(
        BlokeBotDbContext db,
        Competition competition,
        CompetitionEntrant entrant,
        int placement,
        PointAmount points,
        string achievementKey,
        DateTime now,
        CancellationToken ct
    ) =>
        GrantRewardAsync(
            db,
            competition,
            entrant,
            CompetitionRewardKind.Placement,
            $"placement:{placement}",
            placement,
            null,
            points,
            achievementKey,
            $"Competition placement: {competition.Name} (#{placement})",
            now,
            ct
        );

    private static Task GrantWinMilestoneAsync(
        BlokeBotDbContext db,
        Competition competition,
        CompetitionEntrant entrant,
        CompetitionMilestoneRewardRule rule,
        DateTime now,
        CancellationToken ct
    ) =>
        GrantRewardAsync(
            db,
            competition,
            entrant,
            CompetitionRewardKind.WinMilestone,
            $"wins:{rule.WinsRequired}",
            null,
            rule.WinsRequired,
            PointAmount.ParseAbsolute(rule.Points),
            rule.AchievementKey,
            $"Competition win milestone: {competition.Name} ({rule.WinsRequired} wins)",
            now,
            ct
        );

    private static async Task GrantRewardAsync(
        BlokeBotDbContext db,
        Competition competition,
        CompetitionEntrant entrant,
        CompetitionRewardKind kind,
        string rewardKey,
        int? placement,
        int? winsRequired,
        PointAmount points,
        string achievementKey,
        string note,
        DateTime now,
        CancellationToken ct
    )
    {
        foreach (var member in entrant.Members)
        {
            if (
                competition.Rewards.Any(x =>
                    x.EntrantId == entrant.Id && x.Login == member.Login && x.RewardKey == rewardKey
                )
            )
            {
                continue;
            }
            if (!points.IsZero)
            {
                var balance = db.PointBalances.Local.SingleOrDefault(x =>
                    x.HostId == competition.HostId && x.Login == member.Login
                );
                balance ??= await db.PointBalances.SingleOrDefaultAsync(
                    x => x.HostId == competition.HostId && x.Login == member.Login,
                    ct
                );
                var current = balance is null
                    ? PointAmount.Zero
                    : PointAmount.ParseAbsolute(balance.Amount);
                var updated = current.Add(points);
                if (balance is null)
                {
                    balance = new PointBalance
                    {
                        HostId = competition.HostId,
                        Login = member.Login,
                        Amount = updated.ToString(),
                        UpdatedAtUtc = now,
                    };
                    _ = db.PointBalances.Add(balance);
                }
                else
                {
                    balance.Amount = updated.ToString();
                    balance.UpdatedAtUtc = now;
                }
                _ = db.PointLedgerEntries.Add(
                    new PointLedgerEntry
                    {
                        HostId = competition.HostId,
                        CreatedAtUtc = now,
                        Kind = PointLedgerKind.CompetitionReward,
                        Login = member.Login,
                        Delta = points.ToString(),
                        BalanceAfter = updated.ToString(),
                        Note = note,
                        OperationKey =
                            $"competition:{competition.PublicId:N}:{rewardKey}:{member.Login}",
                    }
                );
            }
            competition.Rewards.Add(
                new CompetitionRewardReceipt
                {
                    HostId = competition.HostId,
                    EntrantId = entrant.Id,
                    TwitchUserId = member.TwitchUserId,
                    Login = member.Login,
                    Kind = kind,
                    RewardKey = rewardKey,
                    Placement = placement,
                    WinsRequired = winsRequired,
                    PointsGranted = points.ToString(),
                    AchievementKey = achievementKey,
                    GrantedAtUtc = now,
                }
            );
        }
    }

    private async Task ReconcileAchievementsAsync(Competition competition, CancellationToken ct)
    {
        foreach (
            var reward in competition.Rewards.Where(x =>
                x.AchievementKey.Length > 0 && x.AchievementGrantedAtUtc == null
            )
        )
        {
            var result = await achievements.GrantAsync(
                new(
                    competition.HostId,
                    "competition-reward",
                    $"{competition.PublicId:N}:{reward.RewardKey}:{reward.Login}",
                    new(reward.AchievementKey),
                    new(reward.TwitchUserId, reward.Login, reward.Login),
                    new DateTimeOffset(reward.GrantedAtUtc, TimeSpan.Zero)
                ),
                ct
            );
            if (result is not CommunityExternalGrantOutcome.Granted)
            {
                continue;
            }
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            _ = await db
                .CompetitionRewardReceipts.Where(x =>
                    x.Id == reward.Id && x.AchievementGrantedAtUtc == null
                )
                .ExecuteUpdateAsync(
                    setters =>
                        setters.SetProperty(
                            x => x.AchievementGrantedAtUtc,
                            timeProvider.GetUtcNow().UtcDateTime
                        ),
                    ct
                );
        }
    }

    private async Task PublishAsync(
        CompetitionLifecycleEvent lifecycle,
        CancellationToken ct,
        bool pointsChanged = false
    )
    {
        if (pointsChanged)
        {
            _ = await events.PublishAsync(AppEventKind.PointsChanged, ct);
        }
        foreach (var observer in observers)
        {
            await observer.CompetitionChangedAsync(lifecycle, ct);
        }
    }

    private static CompetitionLifecycleEvent AddEvent(
        Competition competition,
        Guid operationId,
        CompetitionEventKind kind,
        object payload,
        DateTime now
    )
    {
        var json = JsonSerializer.Serialize(payload);
        competition.Events.Add(
            new CompetitionDomainEvent
            {
                HostId = competition.HostId,
                CompetitionPublicId = competition.PublicId,
                OperationKey = $"{kind}:{operationId:N}",
                SchemaVersion = 1,
                Kind = kind,
                PublicPayload = json,
                OccurredAtUtc = now,
            }
        );
        return new(
            operationId,
            competition.HostId,
            new(competition.PublicId),
            kind,
            json,
            new DateTimeOffset(now, TimeSpan.Zero)
        );
    }

    private static void AddAudit(
        Competition competition,
        Guid operationId,
        CompetitionAuditAction action,
        CompetitionActor actor,
        string reason,
        DateTime now
    ) =>
        competition.Audits.Add(
            new CompetitionAudit
            {
                HostId = competition.HostId,
                OperationId = operationId,
                Action = action,
                ActorTwitchUserId = actor.TwitchUserId.Trim(),
                ActorLogin = CommunityInput.NormalizeLogin(actor.Login),
                PrivateReason = reason.Trim(),
                OccurredAtUtc = now,
            }
        );

    private static IQueryable<Competition> Query(BlokeBotDbContext db) =>
        db
            .Competitions.AsSplitQuery()
            .Include(x => x.Entrants)
                .ThenInclude(x => x.Members)
            .Include(x => x.Matches)
            .Include(x => x.Audits)
            .Include(x => x.MilestoneRewards)
            .Include(x => x.Rewards);

    private static CompetitionModeratorView ToModerator(
        Competition competition,
        string hostLogin
    ) =>
        new(
            ToView(competition, hostLogin),
            competition.PrivateLobbyInformation,
            PointAmount.ParseAbsolute(competition.MinimumPoints),
            PointAmount.ParseAbsolute(competition.WinnerPoints),
            PointAmount.ParseAbsolute(competition.RunnerUpPoints),
            competition.WinnerAchievementKey,
            competition.RunnerUpAchievementKey,
            competition
                .MilestoneRewards.OrderBy(x => x.WinsRequired)
                .Select(x => new CompetitionMilestoneRewardView(
                    x.WinsRequired,
                    PointAmount.ParseAbsolute(x.Points),
                    x.AchievementKey
                ))
                .ToArray(),
            competition.ReminderHoursBefore,
            competition.ReminderMessage,
            competition
                .Audits.OrderByDescending(x => x.OccurredAtUtc)
                .Select(x => new CompetitionAuditView(
                    x.Action,
                    x.ActorLogin,
                    x.PrivateReason,
                    x.OccurredAtUtc
                ))
                .ToArray()
        );

    private static CompetitionView ToView(Competition competition, string hostLogin)
    {
        var entrants = competition
            .Entrants.OrderBy(x => x.RegisteredAtUtc)
            .Select(x => new CompetitionEntrantView(
                new(x.PublicId),
                x.Name,
                x.SeedRank,
                x.Members.Select(m => new CompetitionMemberView(m.Login, m.DisplayName)).ToArray()
            ))
            .ToArray();
        var names = competition.Entrants.ToDictionary(x => x.Id, x => x.Name);
        var ids = competition.Entrants.ToDictionary(
            x => x.Id,
            x => new CompetitionEntrantId(x.PublicId)
        );
        var matches = competition
            .Matches.OrderBy(x => x.Round)
            .ThenBy(x => x.Position)
            .Select(x => new CompetitionMatchView(
                new(x.PublicId),
                x.Round,
                x.Position,
                x.EntrantAId is { } a ? ids[a] : null,
                x.EntrantBId is { } b ? ids[b] : null,
                x.EntrantAId is { } ai ? names[ai] : "To be decided",
                x.EntrantBId is { } bi ? names[bi] : "To be decided",
                x.ScoreA,
                x.ScoreB,
                x.Status,
                x.ScheduledAtUtc
            ))
            .ToArray();
        return new(
            new(competition.PublicId),
            hostLogin,
            competition.Name,
            competition.Description,
            competition.Format,
            competition.EntryKind,
            competition.Status,
            competition.Seeding,
            competition.Tiebreak,
            competition.Capacity,
            competition.TeamSize,
            competition.Seed,
            competition.AlgorithmVersion,
            competition.WinPoints,
            competition.DrawPoints,
            competition.LossPoints,
            competition.Revision,
            entrants,
            matches,
            Standings(competition)
                .Select(
                    (x, index) =>
                        new CompetitionStandingView(
                            index + 1,
                            ids[x.EntrantId],
                            names[x.EntrantId],
                            x.Played,
                            x.Wins,
                            x.Draws,
                            x.Losses,
                            x.ScoreFor,
                            x.ScoreAgainst,
                            x.Points
                        )
                )
                .ToArray(),
            competition.CompletedAtUtc,
            competition.ArchivedAtUtc
        );
    }

    private sealed record Standing(
        long EntrantId,
        int Played,
        int Wins,
        int Draws,
        int Losses,
        int ScoreFor,
        int ScoreAgainst,
        int Points
    );

    private static IReadOnlyList<Standing> Standings(Competition competition)
    {
        var rows = competition.Entrants.ToDictionary(
            x => x.Id,
            x => new Standing(x.Id, 0, 0, 0, 0, 0, 0, 0)
        );
        foreach (
            var match in competition.Matches.Where(x =>
                x.Status == CompetitionMatchStatus.Confirmed
                && x.EntrantAId != null
                && x.EntrantBId != null
            )
        )
        {
            var a = rows[match.EntrantAId!.Value];
            var b = rows[match.EntrantBId!.Value];
            var scoreA = match.ScoreA!.Value;
            var scoreB = match.ScoreB!.Value;
            var draw = scoreA == scoreB;
            var aWin = scoreA > scoreB;
            rows[a.EntrantId] = a with
            {
                Played = a.Played + 1,
                Wins = a.Wins + (aWin ? 1 : 0),
                Draws = a.Draws + (draw ? 1 : 0),
                Losses = a.Losses + (!draw && !aWin ? 1 : 0),
                ScoreFor = a.ScoreFor + scoreA,
                ScoreAgainst = a.ScoreAgainst + scoreB,
                Points =
                    a.Points
                    + (
                        draw ? competition.DrawPoints
                        : aWin ? competition.WinPoints
                        : competition.LossPoints
                    ),
            };
            rows[b.EntrantId] = b with
            {
                Played = b.Played + 1,
                Wins = b.Wins + (!draw && !aWin ? 1 : 0),
                Draws = b.Draws + (draw ? 1 : 0),
                Losses = b.Losses + (aWin ? 1 : 0),
                ScoreFor = b.ScoreFor + scoreB,
                ScoreAgainst = b.ScoreAgainst + scoreA,
                Points =
                    b.Points
                    + (
                        draw ? competition.DrawPoints
                        : !aWin ? competition.WinPoints
                        : competition.LossPoints
                    ),
            };
        }
        return competition.Tiebreak == CompetitionTiebreak.ScoreDifferenceThenScoreFor
            ? rows
                .Values.OrderByDescending(x => x.Points)
                .ThenByDescending(x => x.ScoreFor - x.ScoreAgainst)
                .ThenByDescending(x => x.ScoreFor)
                .ThenBy(x => x.EntrantId)
                .ToArray()
            : rows
                .Values.OrderByDescending(x => x.Points)
                .ThenByDescending(x => x.ScoreFor)
                .ThenByDescending(x => x.Wins)
                .ThenBy(x => x.EntrantId)
                .ToArray();
    }

    private Task<bool> FeatureEnabledAsync(int hostId, CancellationToken ct) =>
        HostFeatureAvailability.IsEnabledAsync(dbFactory, hostId, _requiredFeature, ct);

    private static Task<bool> FeatureEnabledAsync(
        BlokeBotDbContext db,
        int hostId,
        CancellationToken ct
    ) =>
        db.Hosts.AnyAsync(
            x => x.Id == hostId && (x.EnabledFeatures & _requiredFeature) == _requiredFeature,
            ct
        );

    private static CompetitionOutcome.Invalid? Validate(CompetitionDraft draft) =>
        string.IsNullOrWhiteSpace(draft.Name) || draft.Name.Trim().Length > 160
            ? new("Competition name must be between 1 and 160 characters.")
        : draft.Description.Trim().Length > 2000
            ? new("Description cannot exceed 2,000 characters.")
        : draft.Capacity is < 2 or > 128 ? new("Capacity must be between 2 and 128 entrants.")
        : draft.EntryKind == CompetitionEntryKind.Team && draft.TeamSize is < 2 or > 32
            ? new("Team size must be between 2 and 32 members.")
        : draft.WinPoints < 0 || draft.DrawPoints < 0 || draft.LossPoints < 0
            ? new("Standing points cannot be negative.")
        : draft.ReminderHoursBefore is < 0 or > 168
            ? new("Reminder lead time must be between 0 and 168 hours.")
        : draft.ReminderMessage.Trim().Length is 0 or > 500
            ? new("Reminder message must be between 1 and 500 characters.")
        : draft.MilestoneRewards.Count > 8
            ? new("No more than 8 win milestone rewards can be configured.")
        : draft.MilestoneRewards.Any(x => x.WinsRequired <= 0)
            ? new("Win milestone thresholds must be positive.")
        : draft.MilestoneRewards.Select(x => x.WinsRequired).Distinct().Count()
        != draft.MilestoneRewards.Count
            ? new("Win milestone thresholds must be unique.")
        : draft.MilestoneRewards.Any(x =>
            x.Points.IsZero && string.IsNullOrWhiteSpace(x.AchievementKey)
        )
            ? new("Each win milestone must grant points or an achievement.")
        : string.IsNullOrWhiteSpace(draft.Seed) || draft.Seed.Trim().Length > 128
            ? new("A reproducible seed of at most 128 characters is required.")
        : draft.PrivateLobbyInformation.Trim().Length > 1000
            ? new("Private lobby information cannot exceed 1,000 characters.")
        : null;

    private static CompetitionOutcome.Invalid? Validate(CompetitionRegistration registration) =>
        string.IsNullOrWhiteSpace(registration.Name) || registration.Name.Trim().Length > 160
            ? new("Entrant name must be between 1 and 160 characters.")
        : registration.Members.Count == 0 ? new("At least one member is required.")
        : registration.Members.Any(x =>
            string.IsNullOrWhiteSpace(x.TwitchUserId)
            || !CommunityInput.IsValidLogin(CommunityInput.NormalizeLogin(x.Login))
        )
            ? new("Every member needs a Twitch user ID and valid login.")
        : registration.Members.Any(x =>
            x.DisplayName.Trim().Length is 0 or > 128 || x.PrivateContact.Trim().Length > 500
        )
            ? new("Member display names and private contact details exceed supported limits.")
        : null;

    private static string NormalizeKey(string value) => value.Trim().ToLowerInvariant();
}
