using BlokeBot.Core.Features.Points;
using BlokeBot.Core.Features.Points.Giveaways;
using BlokeBot.Functional;
using BlokeBot.Persistence.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class PointsGiveawayRehydrationTests : PointsGiveawaySchedulerTestBase
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
        _ = giveaway.CompletedAtUtc.ShouldNotBeNull();
        (await db.PointsGiveawayWinners.CountAsync(x => x.GiveawayId == giveawayId)).ShouldBe(0);
        (await db.PointLedgerEntries.CountAsync(x => x.GiveawayId == giveawayId)).ShouldBe(0);
        (await db.PointBalances.CountAsync(x => x.HostId == hostId)).ShouldBe(0);
    }

    [Test]
    public async Task OverdueGiveaways_ExpiringConcurrently_UseDistinctFactoryConnections()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var firstHostId = await SeedHostAsync(dbFactory, "first-streamer");
        var secondHostId = await SeedHostAsync(dbFactory, "second-streamer");
        var firstStartedAtUtc = DateTime.UtcNow.AddMinutes(-10);
        var firstEndsAtUtc = DateTime.UtcNow.AddMinutes(-5);
        var firstGiveawayId = await SeedGiveawayAsync(
            dbFactory,
            firstHostId,
            firstStartedAtUtc,
            firstEndsAtUtc
        );
        var secondStartedAtUtc = DateTime.UtcNow.AddMinutes(-9);
        var secondEndsAtUtc = DateTime.UtcNow.AddMinutes(-4);
        var secondGiveawayId = await SeedGiveawayAsync(
            dbFactory,
            secondHostId,
            secondStartedAtUtc,
            secondEndsAtUtc
        );
        var recordingFactory = new RecordingDbContextFactory(dbFactory);
        var scheduler = CreateScheduler(recordingFactory);
        await using var firstContext = await recordingFactory.CreateDbContextAsync();
        await using var secondContext = await recordingFactory.CreateDbContextAsync();
        await firstContext.Database.OpenConnectionAsync();
        await secondContext.Database.OpenConnectionAsync();

        firstContext
            .Database.GetDbConnection()
            .ShouldNotBeSameAs(secondContext.Database.GetDbConnection());
        await Task.WhenAll(
            Task.Run(() =>
                scheduler.ExecuteScheduleAsync(
                    new PointsGiveawaySchedule(
                        firstGiveawayId,
                        firstHostId,
                        "first-streamer",
                        firstStartedAtUtc,
                        firstEndsAtUtc,
                        null
                    ),
                    CancellationToken.None
                )
            ),
            Task.Run(() =>
                scheduler.ExecuteScheduleAsync(
                    new PointsGiveawaySchedule(
                        secondGiveawayId,
                        secondHostId,
                        "second-streamer",
                        secondStartedAtUtc,
                        secondEndsAtUtc,
                        null
                    ),
                    CancellationToken.None
                )
            )
        );

        await using var db = await dbFactory.CreateDbContextAsync();
        var statuses = await db
            .PointsGiveaways.Where(x => x.Id == firstGiveawayId || x.Id == secondGiveawayId)
            .Select(x => x.Status)
            .ToArrayAsync();
        statuses.ShouldBe(
            [PointsGiveawayStatus.Expired, PointsGiveawayStatus.Expired],
            ignoreOrder: true
        );
        var connections = recordingFactory.Connections;
        connections.Length.ShouldBeGreaterThanOrEqualTo(4);
        connections
            .Distinct(ReferenceEqualityComparer.Instance)
            .Count()
            .ShouldBe(connections.Length);
    }

    [Test]
    public void SqliteBusyAndLocked_Classifying_AreTransientWithDirectEfWrapping()
    {
        var busy = new SqliteException("database busy", SQLitePCL.raw.SQLITE_BUSY);
        var locked = new SqliteException("database locked", SQLitePCL.raw.SQLITE_LOCKED);
        var wrappedLocked = new DbUpdateException("update locked", locked);

        PointsGiveawaySchedulerFailureClassifier.IsTransient(busy).ShouldBeTrue();
        PointsGiveawaySchedulerFailureClassifier.IsTransient(locked).ShouldBeTrue();
        PointsGiveawaySchedulerFailureClassifier.IsTransient(wrappedLocked).ShouldBeTrue();
    }

    [Test]
    public async Task ClassifiedTransientStorageFailure_Rehydrating_RetriesProductionOperation()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var now = new DateTimeOffset(2026, 7, 11, 12, 0, 0, TimeSpan.Zero);
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var giveawayId = await SeedGiveawayAsync(
            dbFactory,
            hostId,
            now.UtcDateTime,
            now.AddHours(1).UtcDateTime
        );
        var transient = new SqliteException("database busy", SQLitePCL.raw.SQLITE_BUSY);
        var flakyFactory = new FailingOnceDbContextFactory(dbFactory, transient);
        var timeProvider = new StaticTimeProvider(now);
        var changes = new PointsChangeNotifier(TestEventBus.Create<AppEventKind>());
        var logger = new RecordingLogger<PointsGiveawayScheduler>();
        var scheduler = new PointsGiveawayScheduler(
            new PointsGiveawaySchedulerOperations(
                flakyFactory,
                CreateDrawService(flakyFactory),
                new PointsGiveawayMessageFormatter(),
                new PointsGiveawayChangeNotification(new PointsGiveawayChangeNotifier(changes)),
                timeProvider
            ),
            new ReplyOnlyPointsGiveawaySchedulerNotification(),
            new PointsGiveawaySchedulerRecoveryPolicy { RetryDelay = TimeSpan.Zero },
            timeProvider,
            logger
        );

        await scheduler.RehydrateAsync(CancellationToken.None);

        PointsGiveawaySchedulerFailureClassifier.IsTransient(transient).ShouldBeTrue();
        flakyFactory.Attempts.ShouldBe(2);
        scheduler.IsScheduled(giveawayId).ShouldBeTrue();
        logger.Entries.ShouldContain(static entry =>
            entry.Level == LogLevel.Error
            && entry.Message.Contains("retry scheduled", StringComparison.Ordinal)
            && entry.Exception == null
        );
        logger.Entries.ShouldContain(static entry =>
            entry.Level == LogLevel.Information
            && entry.Message.Contains("recovered on attempt 2", StringComparison.Ordinal)
        );

        await scheduler.StopAsync(CancellationToken.None);
    }

    [Test]
    public async Task SqliteConstraintWrappedByDbUpdate_Rehydrating_IsTerminalWithoutRetry()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var constraint = new SqliteException("constraint secret", SQLitePCL.raw.SQLITE_CONSTRAINT);
        var terminal = new DbUpdateException("update secret", constraint);
        var genericDatabaseFailure = new TestDatabaseException();
        var flakyFactory = new FailingOnceDbContextFactory(dbFactory, terminal);
        var timeProvider = new StaticTimeProvider(
            new DateTimeOffset(2026, 7, 11, 12, 0, 0, TimeSpan.Zero)
        );
        var changes = new PointsChangeNotifier(TestEventBus.Create<AppEventKind>());
        var logger = new RecordingLogger<PointsGiveawayScheduler>();
        var scheduler = new PointsGiveawayScheduler(
            new PointsGiveawaySchedulerOperations(
                flakyFactory,
                CreateDrawService(flakyFactory),
                new PointsGiveawayMessageFormatter(),
                new PointsGiveawayChangeNotification(new PointsGiveawayChangeNotifier(changes)),
                timeProvider
            ),
            new ReplyOnlyPointsGiveawaySchedulerNotification(),
            new PointsGiveawaySchedulerRecoveryPolicy { RetryDelay = TimeSpan.Zero },
            timeProvider,
            logger
        );

        var thrown = await Should.ThrowAsync<PointsGiveawaySchedulerUnhealthyException>(() =>
            scheduler.RehydrateAsync(CancellationToken.None)
        );

        PointsGiveawaySchedulerFailureClassifier.IsTransient(constraint).ShouldBeFalse();
        PointsGiveawaySchedulerFailureClassifier.IsTransient(terminal).ShouldBeFalse();
        PointsGiveawaySchedulerFailureClassifier
            .IsTransient(genericDatabaseFailure)
            .ShouldBeFalse();
        PointsGiveawaySchedulerFailureClassifier.IsNotificationFailure(terminal).ShouldBeFalse();
        PointsGiveawaySchedulerFailureClassifier
            .IsNotificationFailure(genericDatabaseFailure)
            .ShouldBeFalse();
        PointsGiveawaySchedulerFailureClassifier
            .ClassifyUnhealthy(constraint)
            .ShouldBe(PointsGiveawaySchedulerFailureClassification.Terminal);
        PointsGiveawaySchedulerFailureClassifier
            .ClassifyUnhealthy(genericDatabaseFailure)
            .ShouldBe(PointsGiveawaySchedulerFailureClassification.Terminal);
        flakyFactory.Attempts.ShouldBe(1);
        var report =
            thrown.Report.ShouldBeOfType<PointsGiveawaySchedulerUnhealthyReport.Rehydration>();
        report.Classification.ShouldBe(PointsGiveawaySchedulerFailureClassification.Terminal);
        report.Cause.ShouldBeSameAs(terminal);
        thrown.InnerException.ShouldBeSameAs(terminal);
        var diagnostic = logger.Entries.Single();
        diagnostic.Level.ShouldBe(LogLevel.Critical);
        diagnostic.Exception.ShouldBeNull();
        diagnostic.Message.ShouldNotContain("constraint secret");
        diagnostic.Message.ShouldNotContain("update secret");
        diagnostic.Message.ShouldNotContain("retry scheduled");
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
            new StaticTimeProvider(new DateTimeOffset(2026, 7, 11, 12, 0, 0, TimeSpan.Zero)),
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
        logger.Entries.ShouldContain(static entry =>
            entry.Level == LogLevel.Error
            && entry.Message.Contains("Expire failed", StringComparison.Ordinal)
            && entry.Message.Contains("retry scheduled", StringComparison.Ordinal)
            && entry.Exception == null
        );
        logger.Entries.ShouldContain(static entry =>
            entry.Level == LogLevel.Information
            && entry.Message.Contains("Expire recovered", StringComparison.Ordinal)
        );
    }

    [Test]
    public async Task ClassifiedTransientDrawFailure_RunningSchedule_RetriesUntilDrawCompletes()
    {
        var now = new DateTimeOffset(2026, 7, 11, 12, 0, 0, TimeSpan.Zero);
        var operations = new RecordingSchedulerOperations();
        operations.DrawOutcomes.Enqueue(Failure<PointsGiveawayDrawOutcome>());
        operations.DrawOutcomes.Enqueue(
            Result<PointsGiveawayDrawOutcome, PointsGiveawaySchedulerTransientFailure>.Success(
                new PointsGiveawayDrawOutcome.NoEntrants(new PointsSettings { HostId = 7 })
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
        logger.Entries.ShouldContain(static entry =>
            entry.Level == LogLevel.Error
            && entry.Message.Contains("Draw failed", StringComparison.Ordinal)
            && entry.Message.Contains("retry scheduled", StringComparison.Ordinal)
            && entry.Exception == null
        );
        logger.Entries.ShouldContain(static entry =>
            entry.Level == LogLevel.Information
            && entry.Message.Contains("Draw recovered", StringComparison.Ordinal)
        );
    }

    [Test]
    public async Task ProgrammingFault_RunningScheduledGiveaway_IsObservedUnhealthyWithoutRetry()
    {
        var now = new DateTimeOffset(2026, 7, 11, 12, 0, 0, TimeSpan.Zero);
        var expected = new NullReferenceException("programming secret");
        var operations = new RecordingSchedulerOperations { DrawException = expected };
        var logger = new RecordingLogger<PointsGiveawayScheduler>();
        var scheduler = CreateScheduler(
            operations,
            new AutoAdvanceTimeProvider(now),
            new ReplyOnlyPointsGiveawaySchedulerNotification(),
            logger
        );

        scheduler.Schedule(ScheduleEndingAfter(now));
        var thrown = await Should.ThrowAsync<PointsGiveawaySchedulerUnhealthyException>(() =>
            scheduler.ThrowWhenUnhealthyAsync(CancellationToken.None)
        );

        operations.DrawAttempts.ShouldBe(1);
        scheduler.IsScheduled(42).ShouldBeFalse();
        var report =
            thrown.Report.ShouldBeOfType<PointsGiveawaySchedulerUnhealthyReport.Giveaway>();
        report.GiveawayId.ShouldBe(42);
        report.Operation.ShouldBe(PointsGiveawaySchedulerOperation.Draw);
        report.Classification.ShouldBe(PointsGiveawaySchedulerFailureClassification.Unexpected);
        ReferenceEquals(report.Cause, expected).ShouldBeTrue();
        ReferenceEquals(thrown.InnerException, expected).ShouldBeTrue();
        var diagnostic = logger.Entries.Single();
        diagnostic.Level.ShouldBe(LogLevel.Critical);
        diagnostic.Exception.ShouldBeNull();
        diagnostic.Message.ShouldContain("hosted scheduler will stop");
        diagnostic.Message.ShouldNotContain("programming secret");
    }

    [Test]
    public void AmbiguousCommit_Classifying_IsTerminalRatherThanRetryable()
    {
        var intendedDraw = new PointsGiveawayDrawOutcome.NoEntrants(
            new PointsSettings { HostId = 7 }
        );
        var draw = new PointsGiveawayDrawCommitAmbiguousException(
            42,
            intendedDraw,
            new SqliteException("database busy", SQLitePCL.raw.SQLITE_BUSY)
        );
        var expiration = new PointsGiveawayExpirationCommitAmbiguousException(
            42,
            new SqliteException("database busy", SQLitePCL.raw.SQLITE_BUSY)
        );

        PointsGiveawaySchedulerFailureClassifier.IsTransient(draw).ShouldBeFalse();
        PointsGiveawaySchedulerFailureClassifier
            .ClassifyUnhealthy(draw)
            .ShouldBe(PointsGiveawaySchedulerFailureClassification.Terminal);
        draw.GiveawayId.ShouldBe(42);
        draw.IntendedOutcome.ShouldBeSameAs(intendedDraw);
        PointsGiveawaySchedulerFailureClassifier.IsTransient(expiration).ShouldBeFalse();
        PointsGiveawaySchedulerFailureClassifier
            .ClassifyUnhealthy(expiration)
            .ShouldBe(PointsGiveawaySchedulerFailureClassification.Terminal);
        expiration.GiveawayId.ShouldBe(42);
        expiration.IntendedOutcome.ShouldBe(PointsGiveawayExpirationOutcome.Expired);
    }
}
