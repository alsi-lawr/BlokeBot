using BlokeBot.Core.Features.CommunityProgression;
using BlokeBot.Core.Features.Competitions;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class CompetitionServiceTests
{
    private static readonly DateTimeOffset _now = new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public void Generation_ReproducesRandomBracketsAndRoundRobinSchedules()
    {
        var first = CompetitionSchedule.Order(7, CompetitionSeeding.Random, "same-seed");
        var replay = CompetitionSchedule.Order(7, CompetitionSeeding.Random, "same-seed");
        var other = CompetitionSchedule.Order(7, CompetitionSeeding.Random, "other-seed");

        replay.ShouldBe(first);
        other.ShouldNotBe(first);
        CompetitionSchedule
            .GenerateTournament(first)
            .ShouldBe(CompetitionSchedule.GenerateTournament(replay));
        var league = CompetitionSchedule.GenerateLeague(first);
        league.Count.ShouldBe(21);
        league.SelectMany(x => new[] { x.EntrantA, x.EntrantB }).ShouldNotContain((int?)null);
        league.GroupBy(x => x.Round).ShouldAllBe(round => round.Count() == 3);
    }

    [Test]
    public async Task RoundRobin_UsesConfiguredTiebreakAndGrantsPlacementRewardsOnce()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "alpha", HostFeatureFlags.Competitions);
        await SeedAchievementsAsync(database, hostId);
        var grants = new RecordingGrants();
        var service = Service(database, grants: grants);
        var competition = await CreateAndOpenAsync(
            service,
            hostId,
            CompetitionFormat.RoundRobin,
            achievements: true
        );
        foreach (var login in new[] { "one", "two", "three", "four" })
        {
            await RegisterAsync(service, hostId, competition, login);
            competition = (await service.GetModeratorAsync(hostId, default)).Single().Competition;
        }
        _ = Success(
            await service.StartAsync(hostId, Transition(competition), _now.UtcDateTime, default)
        );
        competition = (await service.GetModeratorAsync(hostId, default)).Single().Competition;
        foreach (var match in competition.Matches)
        {
            _ = Success(
                await service.ConfirmResultAsync(
                    hostId,
                    new(
                        Guid.NewGuid(),
                        competition.Id,
                        match.Id,
                        competition.Revision,
                        match.Position + 2,
                        match.Position,
                        Actor(),
                        "confirmed"
                    ),
                    default
                )
            );
            competition = (await service.GetModeratorAsync(hostId, default)).Single().Competition;
        }
        var completeOperation = Guid.NewGuid();
        var complete = Transition(competition) with { OperationId = completeOperation };
        _ = Success(await service.CompleteAsync(hostId, complete, default));
        _ = Success(await service.CompleteAsync(hostId, complete, default));

        var final = (await service.GetModeratorAsync(hostId, default)).Single().Competition;
        final.Status.ShouldBe(CompetitionStatus.Completed);
        final
            .Standings.Select(x => x.Points)
            .ShouldBe(final.Standings.Select(x => x.Points).OrderDescending());
        await using var verify = await database.CreateDbContextAsync();
        (await verify.CompetitionRewardReceipts.CountAsync()).ShouldBe(2);
        (
            await verify.PointLedgerEntries.CountAsync(x =>
                x.Kind == PointLedgerKind.CompetitionReward
            )
        ).ShouldBe(2);
        (
            await verify.CompetitionRewardReceipts.CountAsync(x =>
                x.AchievementGrantedAtUtc != null
            )
        ).ShouldBe(2);
        grants.Keys.Distinct().Count().ShouldBe(2);
    }

    [Test]
    public async Task TournamentCorrection_ClearsChangedDownstreamResultAndRetainsAudit()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "alpha", HostFeatureFlags.Competitions);
        var service = Service(database);
        var competition = await CreateAndOpenAsync(service, hostId, CompetitionFormat.Tournament);
        foreach (var login in new[] { "one", "two", "three", "four" })
        {
            await RegisterAsync(service, hostId, competition, login);
            competition = (await service.GetModeratorAsync(hostId, default)).Single().Competition;
        }
        _ = Success(
            await service.StartAsync(hostId, Transition(competition), _now.UtcDateTime, default)
        );
        competition = (await service.GetModeratorAsync(hostId, default)).Single().Competition;
        foreach (var semifinal in competition.Matches.Where(x => x.Round == 1))
        {
            _ = Success(
                await service.ConfirmResultAsync(
                    hostId,
                    Result(competition, semifinal, 2, 0),
                    default
                )
            );
            competition = (await service.GetModeratorAsync(hostId, default)).Single().Competition;
        }
        var final = competition.Matches.Single(x => x.Round == 2);
        _ = Success(
            await service.ConfirmResultAsync(hostId, Result(competition, final, 3, 1), default)
        );
        competition = (await service.GetModeratorAsync(hostId, default)).Single().Competition;
        var corrected = competition.Matches.First(x => x.Round == 1);
        _ = Success(
            await service.ConfirmResultAsync(hostId, Result(competition, corrected, 0, 2), default)
        );

        var moderator = (await service.GetModeratorAsync(hostId, default)).Single();
        var resetFinal = moderator.Competition.Matches.Single(x => x.Round == 2);
        resetFinal.Status.ShouldBe(CompetitionMatchStatus.Pending);
        resetFinal.ScoreA.ShouldBeNull();
        moderator.Audit.ShouldContain(x => x.Action == CompetitionAuditAction.ResultCorrected);
        moderator.Audit.ShouldContain(x => x.Action == CompetitionAuditAction.DownstreamReset);
    }

    [Test]
    public async Task DisabledAndOtherHost_BlocksMutationEventsAndPublicDataButRetainsState()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "alpha", HostFeatureFlags.Competitions);
        var otherHostId = await SeedHostAsync(database, "beta", HostFeatureFlags.Competitions);
        var observer = new RecordingObserver();
        var service = Service(database, observer: observer);
        var competition = await CreateAndOpenAsync(service, hostId, CompetitionFormat.RoundRobin);
        observer.Events.ShouldAllBe(value =>
            !value.PublicPayload.Contains("PRIVATE", StringComparison.Ordinal)
        );
        observer.Events.Clear();
        _ = (
            await service.StartAsync(
                otherHostId,
                Transition(competition),
                _now.UtcDateTime,
                default
            )
        ).ShouldBeOfType<CompetitionOutcome.NotFound>();
        await using (var db = await database.CreateDbContextAsync())
        {
            var host = await db.Hosts.SingleAsync(x => x.Id == hostId);
            host.EnabledFeatures = HostFeatureFlags.None;
            _ = await db.SaveChangesAsync();
        }

        _ = (
            await service.StartAsync(hostId, Transition(competition), _now.UtcDateTime, default)
        ).ShouldBeOfType<CompetitionOutcome.FeatureDisabled>();
        observer.Events.ShouldBeEmpty();
        (await service.GetPublicAsync("alpha", default)).ShouldBeNull();
        (await service.GetPublicAsync("beta", default)).ShouldNotBeNull().Active.ShouldBeEmpty();
        (await service.GetModeratorAsync(hostId, default)).ShouldBeEmpty();
        await using (var db = await database.CreateDbContextAsync())
        {
            var host = await db.Hosts.SingleAsync(x => x.Id == hostId);
            host.EnabledFeatures = HostFeatureFlags.Competitions;
            host.CompetitionsAcceptWorkAfterUtc = _now.UtcDateTime;
            _ = await db.SaveChangesAsync();
        }
        var retained = (await service.GetModeratorAsync(hostId, default)).Single().Competition;
        retained.Status.ShouldBe(CompetitionStatus.Registration);
        retained.Matches.ShouldBeEmpty();
        observer.Events.ShouldBeEmpty();
    }

    [Test]
    public async Task ReminderDueWhileOff_IsNotDeliveredOrReplayedAfterEnable()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "alpha", HostFeatureFlags.Competitions);
        var service = Service(database);
        var competition = await CreateAndOpenAsync(service, hostId, CompetitionFormat.RoundRobin);
        await RegisterAsync(service, hostId, competition, "one");
        competition = (await service.GetModeratorAsync(hostId, default)).Single().Competition;
        await RegisterAsync(service, hostId, competition, "two");
        competition = (await service.GetModeratorAsync(hostId, default)).Single().Competition;
        _ = Success(
            await service.StartAsync(
                hostId,
                Transition(competition),
                _now.UtcDateTime.AddDays(1),
                default
            )
        );
        var delivery = new RecordingReminderDelivery();
        var worker = new CompetitionReminderWorker(
            database,
            delivery,
            new FixedTimeProvider(_now),
            NullLogger<CompetitionReminderWorker>.Instance
        );
        await using (var db = await database.CreateDbContextAsync())
        {
            var host = await db.Hosts.SingleAsync(x => x.Id == hostId);
            host.EnabledFeatures = HostFeatureFlags.None;
            _ = await db.SaveChangesAsync();
        }
        (await worker.RunOnceAsync(default)).ShouldBe(0);
        await using (var db = await database.CreateDbContextAsync())
        {
            var host = await db.Hosts.SingleAsync(x => x.Id == hostId);
            host.EnabledFeatures = HostFeatureFlags.Competitions;
            host.CompetitionsAcceptWorkAfterUtc = _now.AddSeconds(1).UtcDateTime;
            _ = await db.SaveChangesAsync();
        }
        (await worker.RunOnceAsync(default)).ShouldBe(0);
        delivery.Calls.ShouldBe(0);
    }

    private static CompetitionService Service(
        SqliteBlokeBotDbFactory database,
        RecordingGrants? grants = null,
        RecordingObserver? observer = null
    ) =>
        new(
            database,
            TestEventBus.Create<AppEventKind>(),
            grants ?? new RecordingGrants(),
            observer is null ? [] : [observer],
            new FixedTimeProvider(_now)
        );

    private static async Task<CompetitionView> CreateAndOpenAsync(
        CompetitionService service,
        int hostId,
        CompetitionFormat format,
        bool achievements = false
    )
    {
        _ = Success(await service.CreateAsync(hostId, Draft(format, achievements), default));
        var competition = (await service.GetModeratorAsync(hostId, default)).Single().Competition;
        _ = Success(await service.OpenRegistrationAsync(hostId, Transition(competition), default));
        return (await service.GetModeratorAsync(hostId, default)).Single().Competition;
    }

    private static async Task RegisterAsync(
        CompetitionService service,
        int hostId,
        CompetitionView competition,
        string login
    ) =>
        _ = Success(
            await service.RegisterAsync(
                hostId,
                new(
                    Guid.NewGuid(),
                    competition.Id,
                    login,
                    null,
                    [new($"{login}-id", login, login, "private")],
                    Actor(),
                    "register"
                ),
                default
            )
        );

    private static CompetitionDraft Draft(CompetitionFormat format, bool achievements) =>
        new(
            Guid.NewGuid(),
            "Community Cup",
            "Public description",
            format,
            CompetitionEntryKind.Individual,
            CompetitionSeeding.Random,
            CompetitionTiebreak.ScoreDifferenceThenScoreFor,
            8,
            1,
            PointAmount.Zero,
            3,
            1,
            0,
            "cup-seed",
            24,
            "Reminder: {competition} round {round} at {scheduled}. {public_url}",
            new(100),
            new(50),
            achievements ? "winner" : string.Empty,
            achievements ? "runner-up" : string.Empty,
            "PRIVATE LOBBY",
            Actor(),
            "create"
        );

    private static CompetitionTransition Transition(CompetitionView competition) =>
        new(Guid.NewGuid(), competition.Id, competition.Revision, Actor(), "transition");

    private static CompetitionResultCommand Result(
        CompetitionView competition,
        CompetitionMatchView match,
        int a,
        int b
    ) =>
        new(
            Guid.NewGuid(),
            competition.Id,
            match.Id,
            competition.Revision,
            a,
            b,
            Actor(),
            "result"
        );

    private static CompetitionActor Actor() => new("streamer-id", "streamer");

    private static CompetitionOutcome.Succeeded Success(CompetitionOutcome outcome) =>
        outcome.ShouldBeOfType<CompetitionOutcome.Succeeded>();

    private static async Task<int> SeedHostAsync(
        SqliteBlokeBotDbFactory database,
        string login,
        HostFeatureFlags features
    )
    {
        await using var db = await database.CreateDbContextAsync();
        var host = new BotHost
        {
            Login = login,
            DisplayName = login,
            TwitchUserId = $"{login}-id",
            EnabledFeatures = features,
            CreatedAtUtc = _now.UtcDateTime,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        return host.Id;
    }

    private static async Task SeedAchievementsAsync(SqliteBlokeBotDbFactory database, int hostId)
    {
        await using var db = await database.CreateDbContextAsync();
        var season = new CommunitySeason
        {
            HostId = hostId,
            PublicId = Guid.NewGuid(),
            CreationOperationId = Guid.NewGuid(),
            Name = "Rewards",
            Status = CommunitySeasonStatus.Draft,
            Visibility = CommunityVisibility.Hidden,
            StartsAtUtc = _now.UtcDateTime,
            EndsAtUtc = _now.AddDays(1).UtcDateTime,
            Revision = 1,
            CreatedAtUtc = _now.UtcDateTime,
            UpdatedAtUtc = _now.UtcDateTime,
        };
        foreach (var key in new[] { "winner", "runner-up" })
        {
            season.Definitions.Add(
                new CommunityDefinition
                {
                    HostId = hostId,
                    PublicId = Guid.NewGuid(),
                    Key = key,
                    Name = key,
                    Kind = CommunityDefinitionKind.Achievement,
                    Scope = CommunityProgressScope.Viewer,
                    CompletionMode = CommunityCompletionMode.OneTime,
                    EventRule = CommunityEventRuleKind.ExternalGrant,
                    Increment = CommunityProgressIncrement.Occurrence,
                    Target = 1,
                    ResetCadence = CommunityResetCadence.None,
                    ResetLocalTime = "00:00",
                    ScheduleRevision = 1,
                    CreatedAtUtc = _now.UtcDateTime,
                }
            );
        }
        _ = db.CommunitySeasons.Add(season);
        _ = await db.SaveChangesAsync();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecordingGrants : ICommunityAchievementGrantService
    {
        public List<string> Keys { get; } = [];

        public Task<CommunityExternalGrantOutcome> GrantAsync(
            CommunityExternalGrantRequest request,
            CancellationToken cancellationToken
        )
        {
            Keys.Add(request.IdempotencyKey);
            return Task.FromResult<CommunityExternalGrantOutcome>(
                new CommunityExternalGrantOutcome.Granted(Guid.NewGuid(), false)
            );
        }
    }

    private sealed class RecordingObserver : ICompetitionLifecycleObserver
    {
        public List<CompetitionLifecycleEvent> Events { get; } = [];

        public ValueTask CompetitionChangedAsync(
            CompetitionLifecycleEvent competitionEvent,
            CancellationToken cancellationToken
        )
        {
            Events.Add(competitionEvent);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingReminderDelivery : ICompetitionReminderDelivery
    {
        public int Calls { get; private set; }

        public Task<bool> DeliverAsync(
            string hostLogin,
            string message,
            IReadOnlyList<CompetitionReminderRecipient> recipients,
            CancellationToken cancellationToken
        )
        {
            Calls++;
            return Task.FromResult(true);
        }
    }
}
