using BlokeBot.Eventing;
using BlokeBot.Features.HostedChannels.Authorization;
using BlokeBot.Features.HostedChannels.Runtime;
using BlokeBot.Features.HostedChannels.Status;
using BlokeBot.Features.Points;
using BlokeBot.Features.Points.Balances;
using BlokeBot.Features.Points.Gambling;
using BlokeBot.Features.Points.Giveaways;
using BlokeBot.Functional;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Tests;

public sealed class PointsGiveawaySchedulerTests
{
    [Test]
    public async Task FutureActiveGiveaway_RehydratingScheduler_ReschedulesWithoutStateChange()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var giveawayId = await SeedGiveawayAsync(
            dbFactory,
            hostId,
            DateTime.UtcNow.AddMinutes(-1),
            DateTime.UtcNow.AddHours(1)
        );
        var scheduler = CreateScheduler(dbFactory);

        await scheduler.RehydrateAsync(CancellationToken.None);

        scheduler.IsScheduled(giveawayId).ShouldBeTrue();
        await using var db = await dbFactory.CreateDbContextAsync();
        var giveaway = await db.PointsGiveaways.SingleAsync(x => x.Id == giveawayId);
        giveaway.Status.ShouldBe(PointsGiveawayStatus.Active);

        await scheduler.StopAsync(CancellationToken.None);
    }

    [Test]
    public async Task OverdueActiveGiveaway_RehydratingScheduler_ExpiresWithoutPayout()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var giveawayId = await SeedGiveawayAsync(
            dbFactory,
            hostId,
            DateTime.UtcNow.AddMinutes(-10),
            DateTime.UtcNow.AddMinutes(-5),
            "entrant"
        );
        var scheduler = CreateScheduler(dbFactory);

        await scheduler.RehydrateAsync(CancellationToken.None);

        scheduler.IsScheduled(giveawayId).ShouldBeFalse();
        await using var db = await dbFactory.CreateDbContextAsync();
        var giveaway = await db.PointsGiveaways.SingleAsync(x => x.Id == giveawayId);
        giveaway.Status.ShouldBe(PointsGiveawayStatus.Expired);
        giveaway.CompletedAtUtc.ShouldNotBeNull();
        (await db.PointsGiveawayWinners.CountAsync(x => x.GiveawayId == giveawayId)).ShouldBe(0);
        (await db.PointLedgerEntries.CountAsync(x => x.GiveawayId == giveawayId)).ShouldBe(0);
        (await db.PointBalances.CountAsync(x => x.HostId == hostId)).ShouldBe(0);
    }

    [Test]
    public async Task RehydrationFailure_Retrying_LoadsAndSchedulesActiveGiveaway()
    {
        var now = new DateTimeOffset(2026, 7, 11, 12, 0, 0, TimeSpan.Zero);
        var schedule = new PointsGiveawaySchedule(
            42,
            7,
            "streamer",
            now.UtcDateTime,
            now.AddHours(1).UtcDateTime,
            null
        );
        var operations = new RecordingSchedulerOperations { Active = [schedule] };
        operations.LoadOutcomes.Enqueue(Failure<IReadOnlyList<PointsGiveawaySchedule>>());
        var logger = new RecordingLogger<PointsGiveawayScheduler>();
        var scheduler = CreateScheduler(
            operations,
            new StaticTimeProvider(now),
            new ReplyOnlyPointsGiveawaySchedulerNotification(),
            logger
        );

        await scheduler.RehydrateAsync(CancellationToken.None);

        operations.LoadAttempts.ShouldBe(2);
        scheduler.IsScheduled(schedule.GiveawayId).ShouldBeTrue();
        logger.Entries.ShouldContain(entry =>
            entry.Level == LogLevel.Error
            && entry.Message.Contains("retry scheduled", StringComparison.Ordinal)
            && entry.Exception == null
        );
        logger.Entries.ShouldContain(entry =>
            entry.Level == LogLevel.Information
            && entry.Message.Contains("recovered on attempt 2", StringComparison.Ordinal)
        );

        await scheduler.StopAsync(CancellationToken.None);
    }

    [Test]
    public async Task CancellationDuringFailedRehydration_StopsWithoutRetryOrFailureReport()
    {
        using var cancellation = new CancellationTokenSource();
        var operations = new RecordingSchedulerOperations
        {
            BeforeLoadResult = cancellation.Cancel,
        };
        operations.LoadOutcomes.Enqueue(Failure<IReadOnlyList<PointsGiveawaySchedule>>());
        var logger = new RecordingLogger<PointsGiveawayScheduler>();
        var scheduler = CreateScheduler(
            operations,
            new StaticTimeProvider(
                new DateTimeOffset(2026, 7, 11, 12, 0, 0, TimeSpan.Zero)
            ),
            new ReplyOnlyPointsGiveawaySchedulerNotification(),
            logger
        );

        var thrown = await Should.ThrowAsync<OperationCanceledException>(() =>
            scheduler.RehydrateAsync(cancellation.Token)
        );

        thrown.CancellationToken.ShouldBe(cancellation.Token);
        operations.LoadAttempts.ShouldBe(1);
        logger.Entries.ShouldBeEmpty();
    }

    [Test]
    public async Task OverdueExpirationFailure_RehydratingScheduler_RetriesUntilExpired()
    {
        var now = new DateTimeOffset(2026, 7, 11, 12, 0, 0, TimeSpan.Zero);
        var schedule = new PointsGiveawaySchedule(
            42,
            7,
            "streamer",
            now.AddMinutes(-10).UtcDateTime,
            now.AddMinutes(-5).UtcDateTime,
            null
        );
        var operations = new RecordingSchedulerOperations { Active = [schedule] };
        operations.ExpirationOutcomes.Enqueue(Failure<PointsGiveawayExpirationOutcome>());
        var logger = new RecordingLogger<PointsGiveawayScheduler>();
        var scheduler = CreateScheduler(
            operations,
            new StaticTimeProvider(now),
            new ReplyOnlyPointsGiveawaySchedulerNotification(),
            logger
        );

        await scheduler.RehydrateAsync(CancellationToken.None);

        operations.ExpirationAttempts.ShouldBe(2);
        scheduler.IsScheduled(schedule.GiveawayId).ShouldBeFalse();
        logger.Entries.ShouldContain(entry =>
            entry.Level == LogLevel.Error
            && entry.Message.Contains("Expire failed", StringComparison.Ordinal)
            && entry.Message.Contains("retry scheduled", StringComparison.Ordinal)
            && entry.Exception == null
        );
        logger.Entries.ShouldContain(entry =>
            entry.Level == LogLevel.Information
            && entry.Message.Contains("Expire recovered", StringComparison.Ordinal)
        );
    }

    [Test]
    public async Task DrawFailure_RunningSchedule_RetriesUntilDrawCompletes()
    {
        var now = new DateTimeOffset(2026, 7, 11, 12, 0, 0, TimeSpan.Zero);
        var operations = new RecordingSchedulerOperations();
        operations.DrawOutcomes.Enqueue(Failure<PointsGiveawayDrawOutcome>());
        operations.DrawOutcomes.Enqueue(
            Result<PointsGiveawayDrawOutcome, PointsGiveawaySchedulerOperationFailure>.Success(
                PointsGiveawayDrawOutcome.NoEntrants(new PointsSettings { HostId = 7 })
            )
        );
        var logger = new RecordingLogger<PointsGiveawayScheduler>();
        var scheduler = CreateScheduler(
            operations,
            new AutoAdvanceTimeProvider(now),
            new ReplyOnlyPointsGiveawaySchedulerNotification(),
            logger
        );

        await scheduler.ExecuteScheduleAsync(ScheduleEndingAfter(now), CancellationToken.None);

        operations.DrawAttempts.ShouldBe(2);
        logger.Entries.ShouldContain(entry =>
            entry.Level == LogLevel.Error
            && entry.Message.Contains("Draw failed", StringComparison.Ordinal)
            && entry.Message.Contains("retry scheduled", StringComparison.Ordinal)
            && entry.Exception == null
        );
        logger.Entries.ShouldContain(entry =>
            entry.Level == LogLevel.Information
            && entry.Message.Contains("Draw recovered", StringComparison.Ordinal)
        );
    }

    [Test]
    public async Task NotificationFailure_AfterDraw_DoesNotRetryOrFailDurableSchedule()
    {
        var now = new DateTimeOffset(2026, 7, 11, 12, 0, 0, TimeSpan.Zero);
        var operations = new RecordingSchedulerOperations();
        operations.DrawNotificationOutcomes.Enqueue(
            Result<Option<string>, PointsGiveawaySchedulerOperationFailure>.Success(
                Option<string>.Some("draw secret message")
            )
        );
        var logger = new RecordingLogger<PointsGiveawayScheduler>();
        var scheduler = CreateScheduler(
            operations,
            new AutoAdvanceTimeProvider(now),
            new ThrowingSchedulerNotification("delivery secret"),
            logger
        );

        await scheduler.ExecuteScheduleAsync(ScheduleEndingAfter(now), CancellationToken.None);

        operations.DrawAttempts.ShouldBe(1);
        operations.DrawNotificationAttempts.ShouldBe(1);
        var failure = logger.Entries.Single(entry => entry.Level == LogLevel.Error);
        failure.Exception.ShouldBeNull();
        failure.Message.ShouldContain("DrawResult notification failed");
        failure.Message.ShouldContain("delivery is not retried");
        failure.Message.ShouldContain("durable schedule processing continues");
        failure.Message.ShouldNotContain("draw secret message");
        failure.Message.ShouldNotContain("delivery secret");
    }

    [Test]
    public async Task MissingOptionalChatDelivery_RunningSchedule_CompletesReplyOnlyPolicy()
    {
        var now = new DateTimeOffset(2026, 7, 11, 12, 0, 0, TimeSpan.Zero);
        var operations = new RecordingSchedulerOperations();
        operations.DrawNotificationOutcomes.Enqueue(
            Result<Option<string>, PointsGiveawaySchedulerOperationFailure>.Success(
                Option<string>.Some("draw result")
            )
        );
        var logger = new RecordingLogger<PointsGiveawayScheduler>();
        var scheduler = CreateScheduler(
            operations,
            new AutoAdvanceTimeProvider(now),
            new ReplyOnlyPointsGiveawaySchedulerNotification(),
            logger
        );

        await scheduler.ExecuteScheduleAsync(ScheduleEndingAfter(now), CancellationToken.None);

        operations.DrawAttempts.ShouldBe(1);
        operations.DrawNotificationAttempts.ShouldBe(1);
        logger.Entries.ShouldBeEmpty();
    }

    [Test]
    public async Task ScheduledGiveaway_CancellingManually_CancelsScheduleAndPersistsStatus()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var giveawayId = await SeedGiveawayAsync(
            dbFactory,
            hostId,
            DateTime.UtcNow,
            DateTime.UtcNow.AddMinutes(5)
        );
        var scheduler = new RecordingGiveawayScheduler();
        var service = CreateGiveawayService(dbFactory, scheduler);

        var result = await service.CancelAsync(hostId, CancellationToken.None);

        result.Success.ShouldBeTrue();
        scheduler.Cancelled.ShouldContain(giveawayId);
        await using var db = await dbFactory.CreateDbContextAsync();
        var giveaway = await db.PointsGiveaways.SingleAsync(x => x.Id == giveawayId);
        giveaway.Status.ShouldBe(PointsGiveawayStatus.Cancelled);
        giveaway.CompletedAtUtc.ShouldNotBeNull();
    }

    [Test]
    public async Task ActiveGiveaway_RequestingCancelOutcome_ReturnsCancelled()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        await SeedGiveawayAsync(dbFactory, hostId, DateTime.UtcNow, DateTime.UtcNow.AddMinutes(5));
        var service = CreateGiveawayService(dbFactory, new RecordingGiveawayScheduler());

        var outcome = await service.CancelOutcomeAsync(hostId, CancellationToken.None);

        outcome.Kind.ShouldBe(PointsGiveawayCancelOutcomeKind.Cancelled);
    }

    [Test]
    public async Task ScheduledGiveaway_EndingManually_CancelsScheduleAndCompletes()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var giveawayId = await SeedGiveawayAsync(
            dbFactory,
            hostId,
            DateTime.UtcNow,
            DateTime.UtcNow.AddMinutes(5)
        );
        var scheduler = new RecordingGiveawayScheduler();
        var service = CreateGiveawayService(dbFactory, scheduler);

        var result = await service.EndAsync(hostId, "streamer", CancellationToken.None);

        result.Success.ShouldBeTrue();
        scheduler.Cancelled.ShouldContain(giveawayId);
        await using var db = await dbFactory.CreateDbContextAsync();
        var giveaway = await db.PointsGiveaways.SingleAsync(x => x.Id == giveawayId);
        giveaway.Status.ShouldBe(PointsGiveawayStatus.Completed);
        giveaway.CompletedAtUtc.ShouldNotBeNull();
    }

    [Test]
    public async Task ActiveGiveaway_RequestingStartOutcome_ReturnsAlreadyActive()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        await SeedGiveawayAsync(dbFactory, hostId, DateTime.UtcNow, DateTime.UtcNow.AddMinutes(5));
        var service = CreateGiveawayService(dbFactory, new RecordingGiveawayScheduler());

        var outcome = await service.StartOutcomeAsync(
            hostId,
            "streamer",
            null,
            CancellationToken.None
        );

        outcome.Kind.ShouldBe(PointsGiveawayStartOutcomeKind.AlreadyActive);
    }

    [Test]
    public async Task RecentCompletedGiveaway_RequestingStartOutcome_ReturnsCooldown()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        await SeedSettingsAsync(
            dbFactory,
            hostId,
            settings => settings.GiveawayCooldownSeconds = 120
        );
        await SeedGiveawayAsync(
            dbFactory,
            hostId,
            DateTime.UtcNow.AddSeconds(-30),
            DateTime.UtcNow.AddSeconds(-10),
            status: PointsGiveawayStatus.Completed
        );
        var service = CreateGiveawayService(dbFactory, new RecordingGiveawayScheduler());

        var outcome = await service.StartOutcomeAsync(
            hostId,
            "streamer",
            null,
            CancellationToken.None
        );

        outcome.Kind.ShouldBe(PointsGiveawayStartOutcomeKind.Cooldown);
        outcome.TimeLeft.ShouldNotBeNull();
    }

    [Test]
    public async Task OfflineStream_RequestingStartOutcome_ReturnsStreamOffline()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var service = CreateGiveawayService(dbFactory, new RecordingGiveawayScheduler());

        var outcome = await service.StartOutcomeAsync(
            hostId,
            "streamer",
            null,
            CancellationToken.None
        );

        outcome.Kind.ShouldBe(PointsGiveawayStartOutcomeKind.StreamOffline);
    }

    [Test]
    public async Task ExistingEntrant_RequestingJoinOutcome_ReturnsDuplicateJoin()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        await SeedGiveawayAsync(
            dbFactory,
            hostId,
            DateTime.UtcNow,
            DateTime.UtcNow.AddMinutes(5),
            "entrant"
        );
        var service = CreateGiveawayService(dbFactory, new RecordingGiveawayScheduler());

        var outcome = await service.JoinOutcomeAsync(
            hostId,
            "streamer",
            "entrant",
            new Dictionary<string, string>(),
            CancellationToken.None
        );

        outcome.Kind.ShouldBe(PointsGiveawayJoinOutcomeKind.DuplicateJoin);
    }

    [Test]
    public async Task IneligibleViewer_RequestingJoinOutcome_ReturnsNotEligible()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        await SeedSettingsAsync(
            dbFactory,
            hostId,
            settings => settings.GiveawayEligibility = PointsEligibilityMode.Subscribers
        );
        await SeedGiveawayAsync(dbFactory, hostId, DateTime.UtcNow, DateTime.UtcNow.AddMinutes(5));
        var service = CreateGiveawayService(dbFactory, new RecordingGiveawayScheduler());

        var outcome = await service.JoinOutcomeAsync(
            hostId,
            "streamer",
            "viewer",
            new Dictionary<string, string>(),
            CancellationToken.None
        );

        outcome.Kind.ShouldBe(PointsGiveawayJoinOutcomeKind.NotEligible);
    }

    [Test]
    public async Task GiveawayWithoutEntrants_Drawing_ReturnsNoEntrants()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var giveawayId = await SeedGiveawayAsync(
            dbFactory,
            hostId,
            DateTime.UtcNow,
            DateTime.UtcNow.AddMinutes(5)
        );
        await SeedSettingsAsync(dbFactory, hostId);
        var service = CreateGiveawayService(dbFactory, new RecordingGiveawayScheduler());

        var outcome = await service.DrawOutcomeAsync(giveawayId, CancellationToken.None);

        outcome.Kind.ShouldBe(PointsGiveawayDrawOutcomeKind.NoEntrants);
    }

    [Test]
    public async Task GiveawayWithEntrant_Drawing_ReturnsWinnerAndPayout()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var giveawayId = await SeedGiveawayAsync(
            dbFactory,
            hostId,
            DateTime.UtcNow,
            DateTime.UtcNow.AddMinutes(5),
            "entrant"
        );
        await SeedSettingsAsync(dbFactory, hostId);
        var service = CreateGiveawayService(dbFactory, new RecordingGiveawayScheduler());

        var outcome = await service.DrawOutcomeAsync(giveawayId, CancellationToken.None);

        outcome.Kind.ShouldBe(PointsGiveawayDrawOutcomeKind.Winners);
        outcome.Winners.Single().Login.ShouldBe("entrant");
        outcome.Winners.Single().Payout.ShouldBe(PointAmount.ParseAbsolute("10"));
    }

    [Test]
    public async Task CompletedGiveaway_DrawingAgain_DoesNotPayTwice()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var giveawayId = await SeedGiveawayAsync(
            dbFactory,
            hostId,
            DateTime.UtcNow,
            DateTime.UtcNow.AddMinutes(5),
            "entrant"
        );
        await SeedSettingsAsync(dbFactory, hostId);
        var service = CreateGiveawayService(dbFactory, new RecordingGiveawayScheduler());

        var first = await service.DrawOutcomeAsync(giveawayId, CancellationToken.None);
        var second = await service.DrawOutcomeAsync(giveawayId, CancellationToken.None);

        first.Success.ShouldBeTrue();
        second.Success.ShouldBeFalse();
        await using var db = await dbFactory.CreateDbContextAsync();
        var giveaway = await db.PointsGiveaways.SingleAsync(x => x.Id == giveawayId);
        giveaway.Status.ShouldBe(PointsGiveawayStatus.Completed);
        (await db.PointsGiveawayWinners.CountAsync(x => x.GiveawayId == giveawayId)).ShouldBe(1);
        (await db.PointLedgerEntries.CountAsync(x => x.GiveawayId == giveawayId)).ShouldBe(1);
        var balance = await db.PointBalances.SingleAsync(x => x.HostId == hostId);
        balance.Login.ShouldBe("entrant");
        balance.Amount.ShouldBe("10");
    }

    private static PointsGiveawayScheduler CreateScheduler(SqliteBlokeBotDbFactory dbFactory)
    {
        var timeProvider = TimeProvider.System;
        var formatter = new PointsGiveawayMessageFormatter();
        var changes = new PointsChangeNotifier(TestEventBus.Create<AppEventKind>());
        return new PointsGiveawayScheduler(
            new PointsGiveawaySchedulerOperations(
                dbFactory,
                CreateDrawService(dbFactory, changes),
                formatter,
                changes,
                timeProvider
            ),
            new ReplyOnlyPointsGiveawaySchedulerNotification(),
            new PointsGiveawaySchedulerRecoveryPolicy { RetryDelay = TimeSpan.Zero },
            timeProvider,
            NullLogger<PointsGiveawayScheduler>.Instance
        );
    }

    private static PointsGiveawayScheduler CreateScheduler(
        IPointsGiveawaySchedulerOperations operations,
        TimeProvider timeProvider,
        IPointsGiveawaySchedulerNotification notification,
        ILogger<PointsGiveawayScheduler> logger
    ) =>
        new(
            operations,
            notification,
            new PointsGiveawaySchedulerRecoveryPolicy { RetryDelay = TimeSpan.Zero },
            timeProvider,
            logger
        );

    private static PointsGiveawayService CreateGiveawayService(
        SqliteBlokeBotDbFactory dbFactory,
        IPointsGiveawayScheduler scheduler
    )
    {
        var httpClientFactory = new FakeHttpClientFactory();
        var options = TwitchBotSettings.FromOptions(new TwitchBotOptions());
        var helix = new TwitchHelixApiClient(httpClientFactory);
        var status = new HostBotStatusService(
            new UnavailableHostBotAppAccessTokenSource(),
            new UnavailableHostBotAccountTokenStatusProvider(),
            helix,
            options
        );
        return new PointsGiveawayService(
            dbFactory,
            CreateDrawService(
                dbFactory,
                new PointsChangeNotifier(TestEventBus.Create<AppEventKind>())
            ),
            new PointsGiveawayEligibilityPolicy(status),
            new PointsGiveawayMessageFormatter(),
            scheduler,
            new PointsChangeNotifier(TestEventBus.Create<AppEventKind>())
        );
    }

    private static PointsGiveawayDrawService CreateDrawService(
        SqliteBlokeBotDbFactory dbFactory,
        PointsChangeNotifier changes
    ) =>
        new(
            dbFactory,
            new PointBalanceService(dbFactory),
            new FixedPointsRandom(),
            changes
        );

    private static PointsGiveawaySchedule ScheduleEndingAfter(DateTimeOffset now) =>
        new(
            42,
            7,
            "streamer",
            now.AddHours(-3).UtcDateTime,
            now.AddHours(1).UtcDateTime,
            null
        );

    private static Result<TValue, PointsGiveawaySchedulerOperationFailure> Failure<TValue>() =>
        Result<TValue, PointsGiveawaySchedulerOperationFailure>.Error(
            new PointsGiveawaySchedulerOperationFailure(
                new InvalidOperationException("operation secret")
            )
        );

    private static async Task<int> SeedHostAsync(SqliteBlokeBotDbFactory dbFactory, string login)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var host = new BotHost
        {
            Login = login,
            DisplayName = login,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.Hosts.Add(host);
        await db.SaveChangesAsync();
        return host.Id;
    }

    private static async Task SeedSettingsAsync(
        SqliteBlokeBotDbFactory dbFactory,
        int hostId,
        Action<PointsSettings>? configure = null
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var settings = new PointsSettings
        {
            HostId = hostId,
            GiveawayMinimumPayout = "10",
            GiveawayMaximumPayout = "10",
            GiveawayWinnerCount = 1,
        };
        configure?.Invoke(settings);
        db.PointsSettings.Add(settings);
        await db.SaveChangesAsync();
    }

    private static async Task<int> SeedGiveawayAsync(
        SqliteBlokeBotDbFactory dbFactory,
        int hostId,
        DateTime startedAtUtc,
        DateTime endsAtUtc,
        string? entrant = null,
        PointsGiveawayStatus status = PointsGiveawayStatus.Active
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var giveaway = new PointsGiveaway
        {
            HostId = hostId,
            Status = status,
            StartedAtUtc = startedAtUtc,
            EndsAtUtc = endsAtUtc,
            MinimumPayout = "10",
            MaximumPayout = "10",
            WinnerCount = 1,
            Eligibility = PointsEligibilityMode.Everyone,
        };
        if (entrant is not null)
        {
            giveaway.Entrants.Add(
                new PointsGiveawayEntrant { Login = entrant, JoinedAtUtc = DateTime.UtcNow }
            );
        }

        db.PointsGiveaways.Add(giveaway);
        await db.SaveChangesAsync();
        return giveaway.Id;
    }

    private sealed class RecordingSchedulerOperations : IPointsGiveawaySchedulerOperations
    {
        public IReadOnlyList<PointsGiveawaySchedule> Active { get; init; } = [];

        public Action? BeforeLoadResult { get; init; }

        public Queue<
            Result<
                IReadOnlyList<PointsGiveawaySchedule>,
                PointsGiveawaySchedulerOperationFailure
            >
        > LoadOutcomes
        { get; } = [];

        public Queue<Result<Option<string>, PointsGiveawaySchedulerOperationFailure>>
            UpdateOutcomes
        { get; } = [];

        public Queue<
            Result<PointsGiveawayDrawOutcome, PointsGiveawaySchedulerOperationFailure>
        > DrawOutcomes
        { get; } = [];

        public Queue<Result<Option<string>, PointsGiveawaySchedulerOperationFailure>>
            DrawNotificationOutcomes
        { get; } = [];

        public Queue<
            Result<
                PointsGiveawayExpirationOutcome,
                PointsGiveawaySchedulerOperationFailure
            >
        > ExpirationOutcomes
        { get; } = [];

        public int LoadAttempts { get; private set; }

        public int UpdateAttempts { get; private set; }

        public int DrawAttempts { get; private set; }

        public int DrawNotificationAttempts { get; private set; }

        public int ExpirationAttempts { get; private set; }

        public IO<
            IReadOnlyList<PointsGiveawaySchedule>,
            PointsGiveawaySchedulerOperationFailure
        > LoadActive() =>
            IO<
                IReadOnlyList<PointsGiveawaySchedule>,
                PointsGiveawaySchedulerOperationFailure
            >.Create(_ =>
            {
                LoadAttempts++;
                BeforeLoadResult?.Invoke();
                return ValueTask.FromResult(Next(LoadOutcomes, Active));
            });

        public IO<Option<string>, PointsGiveawaySchedulerOperationFailure> BuildUpdate(
            int giveawayId,
            DateTime endsAtUtc
        ) =>
            IO<Option<string>, PointsGiveawaySchedulerOperationFailure>.Create(_ =>
            {
                UpdateAttempts++;
                return ValueTask.FromResult(Next(UpdateOutcomes, Option<string>.None));
            });

        public IO<PointsGiveawayDrawOutcome, PointsGiveawaySchedulerOperationFailure> Draw(
            int giveawayId
        ) =>
            IO<
                PointsGiveawayDrawOutcome,
                PointsGiveawaySchedulerOperationFailure
            >.Create(_ =>
            {
                DrawAttempts++;
                return ValueTask.FromResult(
                    Next(DrawOutcomes, PointsGiveawayDrawOutcome.Missing())
                );
            });

        public IO<Option<string>, PointsGiveawaySchedulerOperationFailure> BuildDrawNotification(
            PointsGiveawayDrawOutcome outcome
        ) =>
            IO<Option<string>, PointsGiveawaySchedulerOperationFailure>.Create(_ =>
            {
                DrawNotificationAttempts++;
                return ValueTask.FromResult(
                    Next(DrawNotificationOutcomes, Option<string>.None)
                );
            });

        public IO<
            PointsGiveawayExpirationOutcome,
            PointsGiveawaySchedulerOperationFailure
        > Expire(int giveawayId) =>
            IO<
                PointsGiveawayExpirationOutcome,
                PointsGiveawaySchedulerOperationFailure
            >.Create(_ =>
            {
                ExpirationAttempts++;
                return ValueTask.FromResult(
                    Next(ExpirationOutcomes, PointsGiveawayExpirationOutcome.Expired)
                );
            });

        private static Result<TValue, PointsGiveawaySchedulerOperationFailure> Next<TValue>(
            Queue<Result<TValue, PointsGiveawaySchedulerOperationFailure>> outcomes,
            TValue defaultValue
        ) =>
            outcomes.TryDequeue(out var outcome)
                ? outcome
                : Result<TValue, PointsGiveawaySchedulerOperationFailure>.Success(defaultValue);
    }

    private sealed class ThrowingSchedulerNotification(string failureMessage)
        : IPointsGiveawaySchedulerNotification
    {
        public ValueTask SendAsync(
            PointsGiveawaySchedule schedule,
            string message,
            CancellationToken cancellationToken
        ) => ValueTask.FromException(new InvalidOperationException(failureMessage));
    }

    private class StaticTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        protected DateTimeOffset UtcNow { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow() => UtcNow;

        public override long GetTimestamp() => UtcNow.UtcTicks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }

    private sealed class AutoAdvanceTimeProvider(DateTimeOffset utcNow)
        : StaticTimeProvider(utcNow)
    {
        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period
        )
        {
            if (dueTime > TimeSpan.Zero)
                UtcNow = UtcNow.Add(dueTime);

            callback(state);
            return CompletedTimer.Instance;
        }
    }

    private sealed class CompletedTimer : ITimer
    {
        internal static CompletedTimer Instance { get; } = new();

        public bool Change(TimeSpan dueTime, TimeSpan period) => false;

        public void Dispose() { }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingGiveawayScheduler : IPointsGiveawayScheduler
    {
        public List<int> Cancelled { get; } = [];

        public void Schedule(PointsGiveawaySchedule schedule) { }

        public void Cancel(int giveawayId) => Cancelled.Add(giveawayId);
    }

    private sealed class FixedPointsRandom : IPointsRandom
    {
        public double NextDouble() => 0;

        public int Next(int minValue, int maxValue) => minValue;
    }

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class UnavailableHostBotAccountTokenStatusProvider
        : IHostBotAccountTokenStatusProvider
    {
        public Task<ActiveBotAccountTokenStatus> GetActiveTokenStatusAsync(
            string channelLogin,
            IEnumerable<string?> requiredScopes,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                new ActiveBotAccountTokenStatus(
                    string.Empty,
                    null,
                    TwitchTokenStatusState.Unavailable,
                    null,
                    null,
                    [],
                    [],
                    []
                )
            );
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => NullLoggerScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
        }
    }

    private sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);

    private sealed class NullLoggerScope : IDisposable
    {
        public static readonly NullLoggerScope Instance = new();

        public void Dispose() { }
    }
}
