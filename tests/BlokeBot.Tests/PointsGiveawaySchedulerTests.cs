using System.Collections.Concurrent;
using System.Data.Common;
using System.Net;
using System.Text;
using System.Text.Json;
using BlokeBot.Eventing;
using BlokeBot.Features.HostedChannels.Authorization;
using BlokeBot.Features.HostedChannels.Runtime;
using BlokeBot.Features.HostedChannels.Status;
using BlokeBot.Features.Points;
using BlokeBot.Features.Points.Balances;
using BlokeBot.Features.Points.Gambling;
using BlokeBot.Features.Points.Giveaways;
using BlokeBot.Features.Replies;
using BlokeBot.Functional;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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
                new PointsGiveawayChangeNotification(changes),
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
                new PointsGiveawayChangeNotification(changes),
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
    public async Task ClassifiedTransientDrawFailure_RunningSchedule_RetriesUntilDrawCompletes()
    {
        var now = new DateTimeOffset(2026, 7, 11, 12, 0, 0, TimeSpan.Zero);
        var operations = new RecordingSchedulerOperations();
        operations.DrawOutcomes.Enqueue(Failure<PointsGiveawayDrawOutcome>());
        operations.DrawOutcomes.Enqueue(
            Result<PointsGiveawayDrawOutcome, PointsGiveawaySchedulerTransientFailure>.Success(
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
        var intendedDraw = PointsGiveawayDrawOutcome.NoEntrants(new PointsSettings { HostId = 7 });
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

    [Test]
    public async Task CommitStageCancellation_Drawing_IsTerminalAmbiguousAndNotRetryable()
    {
        var commitCancellation = new CommitCancellationInterceptor();
        await using var dbFactory = await InterceptedSqliteBlokeBotDbFactory.CreateAsync(
            commitCancellation
        );
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        await SeedSettingsAsync(dbFactory, hostId);
        var giveawayId = await SeedGiveawayAsync(
            dbFactory,
            hostId,
            DateTime.UtcNow.AddMinutes(-1),
            DateTime.UtcNow.AddMinutes(1),
            "entrant"
        );
        commitCancellation.FailNextCommit();

        var thrown = await Should.ThrowAsync<PointsGiveawayDrawCommitAmbiguousException>(() =>
            CreateDrawService(dbFactory).DrawOutcomeAsync(giveawayId, CancellationToken.None)
        );

        commitCancellation.CommitAttempts.ShouldBe(1);
        commitCancellation.ObservedCancellationToken.CanBeCanceled.ShouldBeFalse();
        thrown.GiveawayId.ShouldBe(giveawayId);
        thrown.IntendedOutcome.Kind.ShouldBe(PointsGiveawayDrawOutcomeKind.Winners);
        thrown.InnerException.ShouldBeOfType<OperationCanceledException>();
        PointsGiveawaySchedulerFailureClassifier.IsTransient(thrown).ShouldBeFalse();
        PointsGiveawaySchedulerFailureClassifier
            .ClassifyUnhealthy(thrown)
            .ShouldBe(PointsGiveawaySchedulerFailureClassification.Terminal);
        await using var db = await dbFactory.CreateDbContextAsync();
        var giveaway = await db.PointsGiveaways.SingleAsync(x => x.Id == giveawayId);
        giveaway.Status.ShouldBe(PointsGiveawayStatus.Active);
        (await db.PointsGiveawayWinners.CountAsync(x => x.GiveawayId == giveawayId)).ShouldBe(0);
        (await db.PointLedgerEntries.CountAsync(x => x.GiveawayId == giveawayId)).ShouldBe(0);
    }

    [Test]
    public async Task ChangeNotificationFailure_AfterExpiration_DoesNotRetryExpiration()
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
        operations.ChangeNotificationOutcomes.Enqueue(
            Result<
                PointsGiveawayChangeNotificationCompleted,
                PointsGiveawaySchedulerNotificationFailure
            >.Error(
                new PointsGiveawaySchedulerNotificationFailure(
                    new IOException("change notification secret")
                )
            )
        );
        var logger = new RecordingLogger<PointsGiveawayScheduler>();
        var scheduler = CreateScheduler(
            operations,
            new StaticTimeProvider(now),
            new ReplyOnlyPointsGiveawaySchedulerNotification(),
            logger
        );

        await scheduler.RehydrateAsync(CancellationToken.None);

        operations.ExpirationAttempts.ShouldBe(1);
        operations.ChangeNotificationAttempts.ShouldBe(1);
        var failure = logger.Entries.Single(entry => entry.Level == LogLevel.Error);
        failure.Exception.ShouldBeNull();
        failure.Message.ShouldContain("StateChanged notification failed");
        failure.Message.ShouldContain("delivery is not retried");
        failure.Message.ShouldNotContain("change notification secret");
    }

    [Test]
    public async Task NotificationFailure_AfterDraw_DoesNotRetryOrFailDurableSchedule()
    {
        var now = new DateTimeOffset(2026, 7, 11, 12, 0, 0, TimeSpan.Zero);
        var operations = new RecordingSchedulerOperations();
        operations.DrawNotificationOutcomes.Enqueue(
            Result<Option<string>, PointsGiveawaySchedulerNotificationFailure>.Success(
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
    public async Task AcceptedPublicChatNotification_Sending_CompletesWithoutFailureDiagnostic()
    {
        var chat = new ScriptedPublicChatSender(new PublicChatSendOutcome.Accepted());
        var logger = new RecordingLogger<PublicChatPointsGiveawaySchedulerNotification>();
        var notification = new PublicChatPointsGiveawaySchedulerNotification(chat, logger);

        await notification.SendAsync(
            ScheduleEndingAfter(new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero)),
            "public giveaway payload",
            CancellationToken.None
        );

        chat.Messages.ShouldBe(["public giveaway payload"]);
        logger.Entries.ShouldBeEmpty();
    }

    [Test]
    public async Task RejectedPublicChatNotification_Sending_ReportsRedactedNoDelivery()
    {
        var chat = new ScriptedPublicChatSender(new PublicChatSendOutcome.Rejected());
        var logger = new RecordingLogger<PublicChatPointsGiveawaySchedulerNotification>();
        var notification = new PublicChatPointsGiveawaySchedulerNotification(chat, logger);

        await notification.SendAsync(
            ScheduleEndingAfter(new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero)),
            "private giveaway payload",
            CancellationToken.None
        );

        chat.Messages.ShouldBe(["private giveaway payload"]);
        var entry = logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Exception.ShouldBeNull();
        entry.Message.ShouldContain("rejected");
        entry.Message.ShouldNotContain("private giveaway payload");
    }

    [Test]
    public async Task MissingOptionalChatDelivery_RunningSchedule_CompletesReplyOnlyPolicy()
    {
        var now = new DateTimeOffset(2026, 7, 11, 12, 0, 0, TimeSpan.Zero);
        var operations = new RecordingSchedulerOperations();
        operations.DrawNotificationOutcomes.Enqueue(
            Result<Option<string>, PointsGiveawaySchedulerNotificationFailure>.Success(
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
    public async Task TransientPreCommitAndChangeNotificationFailure_RetainsDrawAndPayout()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var now = new DateTimeOffset(2026, 7, 11, 12, 0, 0, TimeSpan.Zero);
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        await SeedSettingsAsync(dbFactory, hostId);
        var startedAtUtc = now.AddHours(-3).UtcDateTime;
        var endsAtUtc = now.AddHours(1).UtcDateTime;
        var giveawayId = await SeedGiveawayAsync(
            dbFactory,
            hostId,
            startedAtUtc,
            endsAtUtc,
            "entrant"
        );
        var changeNotification = new ThrowingGiveawayChangeNotification(
            "change notification secret"
        );
        var flakyFactory = new FailingOnceDbContextFactory(
            dbFactory,
            new SqliteException("database busy", SQLitePCL.raw.SQLITE_BUSY)
        );
        var chat = new RecordingSchedulerNotification();
        var logger = new RecordingLogger<PointsGiveawayScheduler>();
        var timeProvider = new AutoAdvanceTimeProvider(now);
        var scheduler = new PointsGiveawayScheduler(
            new PointsGiveawaySchedulerOperations(
                flakyFactory,
                CreateDrawService(flakyFactory),
                new PointsGiveawayMessageFormatter(),
                changeNotification,
                timeProvider
            ),
            chat,
            new PointsGiveawaySchedulerRecoveryPolicy { RetryDelay = TimeSpan.Zero },
            timeProvider,
            logger
        );

        await scheduler.ExecuteScheduleAsync(
            new PointsGiveawaySchedule(
                giveawayId,
                hostId,
                "streamer",
                startedAtUtc,
                endsAtUtc,
                null
            ),
            CancellationToken.None
        );

        changeNotification.Attempts.ShouldBe(1);
        chat.Messages.ShouldBe(["Giveaway winners: entrant (10)."]);
        logger
            .Entries.Count(entry =>
                entry.Level == LogLevel.Error
                && entry.Message.Contains("Draw failed", StringComparison.Ordinal)
                && entry.Message.Contains("retry scheduled", StringComparison.Ordinal)
            )
            .ShouldBe(1);
        logger.Entries.ShouldContain(entry =>
            entry.Level == LogLevel.Information
            && entry.Message.Contains("Draw recovered", StringComparison.Ordinal)
        );
        var notificationFailure = logger.Entries.Single(entry =>
            entry.Level == LogLevel.Error
            && entry.Message.Contains("StateChanged notification failed", StringComparison.Ordinal)
        );
        notificationFailure.Exception.ShouldBeNull();
        notificationFailure.Message.ShouldNotContain("change notification secret");
        await using var db = await dbFactory.CreateDbContextAsync();
        var giveaway = await db.PointsGiveaways.SingleAsync(x => x.Id == giveawayId);
        giveaway.Status.ShouldBe(PointsGiveawayStatus.Completed);
        (await db.PointsGiveawayWinners.CountAsync(x => x.GiveawayId == giveawayId)).ShouldBe(1);
        (await db.PointLedgerEntries.CountAsync(x => x.GiveawayId == giveawayId)).ShouldBe(1);
        var balance = await db.PointBalances.SingleAsync(x => x.HostId == hostId);
        balance.Login.ShouldBe("entrant");
        balance.Amount.ShouldBe("10");
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
        outcome.StreamLivenessFailure.ShouldBeNull();
    }

    [Test]
    public async Task UnavailableStreamStatus_RequestingStartOutcome_RetainsCauseAndDistinctReply()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var expected = new HttpRequestException("provider secret");
        var service = CreateGiveawayService(
            dbFactory,
            new RecordingGiveawayScheduler(),
            new ThrowingHostBotAppAccessTokenSource(expected)
        );

        var outcome = await service.StartOutcomeAsync(
            hostId,
            "streamer",
            null,
            CancellationToken.None
        );

        outcome.Kind.ShouldBe(PointsGiveawayStartOutcomeKind.StreamLivenessUnavailable);
        var unavailable = outcome.StreamLivenessFailure.ShouldNotBeNull();
        unavailable.Reason.ShouldBe(HostStreamLivenessUnavailableReason.ProviderRequestFailed);
        unavailable.FailureType.ShouldBe(typeof(HttpRequestException).FullName);
        unavailable.Cause.ShouldBeSameAs(expected);
        unavailable.ToString().ShouldNotContain("provider secret");
        outcome.ToString().ShouldNotContain("provider secret");
        JsonSerializer.Serialize(outcome).ShouldNotContain("provider secret");
        var result = new PointsGiveawayMessageFormatter().Reply(outcome, new ReplyDeliveryMap());
        result.Success.ShouldBeFalse();
        result.Message.ShouldBe("Stream status could not be checked right now.");
        result.Message.ShouldNotBe(outcome.Settings.StreamOfflineReply);
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

    private static PointsGiveawayScheduler CreateScheduler(
        IDbContextFactory<BlokeBotDbContext> dbFactory
    )
    {
        var timeProvider = TimeProvider.System;
        var formatter = new PointsGiveawayMessageFormatter();
        var changes = new PointsChangeNotifier(TestEventBus.Create<AppEventKind>());
        return new PointsGiveawayScheduler(
            new PointsGiveawaySchedulerOperations(
                dbFactory,
                CreateDrawService(dbFactory),
                formatter,
                new PointsGiveawayChangeNotification(changes),
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
    )
    {
        return new(
            operations,
            notification,
            new PointsGiveawaySchedulerRecoveryPolicy { RetryDelay = TimeSpan.Zero },
            timeProvider,
            logger
        );
    }

    private static PointsGiveawayService CreateGiveawayService(
        SqliteBlokeBotDbFactory dbFactory,
        IPointsGiveawayScheduler scheduler,
        IHostBotAppAccessTokenSource? appTokens = null
    )
    {
        var httpClientFactory = new FakeHttpClientFactory();
        var options = BotSettings.FromOptions(new BotOptions());
        var helix = new HelixClient(httpClientFactory);
        var status = new HostBotStatusService(
            appTokens ?? new StaticHostBotAppAccessTokenSource(),
            new UnavailableHostBotAccountTokenStatusProvider(),
            helix,
            options
        );
        return new PointsGiveawayService(
            dbFactory,
            CreateDrawService(dbFactory),
            new PointsGiveawayEligibilityPolicy(
                status,
                NullLogger<PointsGiveawayEligibilityPolicy>.Instance
            ),
            new PointsGiveawayMessageFormatter(),
            scheduler,
            new PointsChangeNotifier(TestEventBus.Create<AppEventKind>())
        );
    }

    private static PointsGiveawayDrawService CreateDrawService(
        IDbContextFactory<BlokeBotDbContext> dbFactory
    )
    {
        return new(dbFactory, new PointBalanceService(dbFactory), new FixedPointsRandom());
    }

    private static PointsGiveawaySchedule ScheduleEndingAfter(DateTimeOffset now)
    {
        return new(
            42,
            7,
            "streamer",
            now.AddHours(-3).UtcDateTime,
            now.AddHours(1).UtcDateTime,
            null
        );
    }

    private static Result<TValue, PointsGiveawaySchedulerTransientFailure> Failure<TValue>()
    {
        return Result<TValue, PointsGiveawaySchedulerTransientFailure>.Error(
            new PointsGiveawaySchedulerTransientFailure(
                new SqliteException("database busy", SQLitePCL.raw.SQLITE_BUSY)
            )
        );
    }

    private static async Task<int> SeedHostAsync(
        IDbContextFactory<BlokeBotDbContext> dbFactory,
        string login
    )
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
        IDbContextFactory<BlokeBotDbContext> dbFactory,
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
        IDbContextFactory<BlokeBotDbContext> dbFactory,
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

        public Exception? DrawException { get; init; }

        public Queue<
            Result<IReadOnlyList<PointsGiveawaySchedule>, PointsGiveawaySchedulerTransientFailure>
        > LoadOutcomes { get; } = [];

        public Queue<
            Result<Option<string>, PointsGiveawaySchedulerNotificationFailure>
        > UpdateOutcomes { get; } = [];

        public Queue<
            Result<PointsGiveawayDrawOutcome, PointsGiveawaySchedulerTransientFailure>
        > DrawOutcomes { get; } = [];

        public Queue<
            Result<Option<string>, PointsGiveawaySchedulerNotificationFailure>
        > DrawNotificationOutcomes { get; } = [];

        public Queue<
            Result<PointsGiveawayExpirationOutcome, PointsGiveawaySchedulerTransientFailure>
        > ExpirationOutcomes { get; } = [];

        public Queue<
            Result<
                PointsGiveawayChangeNotificationCompleted,
                PointsGiveawaySchedulerNotificationFailure
            >
        > ChangeNotificationOutcomes { get; } = [];

        public int LoadAttempts { get; private set; }

        public int UpdateAttempts { get; private set; }

        public int DrawAttempts { get; private set; }

        public int DrawNotificationAttempts { get; private set; }

        public int ExpirationAttempts { get; private set; }

        public int ChangeNotificationAttempts { get; private set; }

        public IO<
            IReadOnlyList<PointsGiveawaySchedule>,
            PointsGiveawaySchedulerTransientFailure
        > LoadActive()
        {
            return IO<
                IReadOnlyList<PointsGiveawaySchedule>,
                PointsGiveawaySchedulerTransientFailure
            >.Create(_ =>
            {
                LoadAttempts++;
                BeforeLoadResult?.Invoke();
                return ValueTask.FromResult(Next(LoadOutcomes, Active));
            });
        }

        public IO<Option<string>, PointsGiveawaySchedulerNotificationFailure> BuildUpdate(
            int giveawayId,
            DateTime endsAtUtc
        )
        {
            return IO<Option<string>, PointsGiveawaySchedulerNotificationFailure>.Create(_ =>
            {
                UpdateAttempts++;
                return ValueTask.FromResult(Next(UpdateOutcomes, Option<string>.None));
            });
        }

        public IO<PointsGiveawayDrawOutcome, PointsGiveawaySchedulerTransientFailure> Draw(
            int giveawayId
        )
        {
            return IO<PointsGiveawayDrawOutcome, PointsGiveawaySchedulerTransientFailure>.Create(
                _ =>
                {
                    DrawAttempts++;
                    if (DrawException is { } exception)
                    {
                        return ValueTask.FromException<
                            Result<
                                PointsGiveawayDrawOutcome,
                                PointsGiveawaySchedulerTransientFailure
                            >
                        >(exception);
                    }

                    return ValueTask.FromResult(
                        Next(DrawOutcomes, PointsGiveawayDrawOutcome.Missing())
                    );
                }
            );
        }

        public IO<Option<string>, PointsGiveawaySchedulerNotificationFailure> BuildDrawNotification(
            PointsGiveawayDrawOutcome outcome
        )
        {
            return IO<Option<string>, PointsGiveawaySchedulerNotificationFailure>.Create(_ =>
            {
                DrawNotificationAttempts++;
                return ValueTask.FromResult(Next(DrawNotificationOutcomes, Option<string>.None));
            });
        }

        public IO<PointsGiveawayExpirationOutcome, PointsGiveawaySchedulerTransientFailure> Expire(
            int giveawayId
        )
        {
            return IO<
                PointsGiveawayExpirationOutcome,
                PointsGiveawaySchedulerTransientFailure
            >.Create(_ =>
            {
                ExpirationAttempts++;
                return ValueTask.FromResult(
                    Next(ExpirationOutcomes, PointsGiveawayExpirationOutcome.Expired)
                );
            });
        }

        public IO<
            PointsGiveawayChangeNotificationCompleted,
            PointsGiveawaySchedulerNotificationFailure
        > NotifyChanged()
        {
            return IO<
                PointsGiveawayChangeNotificationCompleted,
                PointsGiveawaySchedulerNotificationFailure
            >.Create(_ =>
            {
                ChangeNotificationAttempts++;
                return ValueTask.FromResult(
                    Next(
                        ChangeNotificationOutcomes,
                        new PointsGiveawayChangeNotificationCompleted()
                    )
                );
            });
        }

        private static Result<TValue, TError> Next<TValue, TError>(
            Queue<Result<TValue, TError>> outcomes,
            TValue defaultValue
        )
        {
            return outcomes.TryDequeue(out var outcome)
                ? outcome
                : Result<TValue, TError>.Success(defaultValue);
        }
    }

    private sealed class FailingOnceDbContextFactory(
        IDbContextFactory<BlokeBotDbContext> inner,
        Exception failure
    ) : IDbContextFactory<BlokeBotDbContext>
    {
        public int Attempts { get; private set; }

        public BlokeBotDbContext CreateDbContext()
        {
            if (++Attempts == 1)
            {
                throw failure;
            }

            return inner.CreateDbContext();
        }

        public Task<BlokeBotDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default
        )
        {
            if (++Attempts == 1)
            {
                return Task.FromException<BlokeBotDbContext>(failure);
            }

            return inner.CreateDbContextAsync(cancellationToken);
        }
    }

    private sealed class RecordingDbContextFactory(IDbContextFactory<BlokeBotDbContext> inner)
        : IDbContextFactory<BlokeBotDbContext>
    {
        private readonly ConcurrentQueue<DbConnection> _connections = [];

        public DbConnection[] Connections => _connections.ToArray();

        public BlokeBotDbContext CreateDbContext()
        {
            var db = inner.CreateDbContext();
            _connections.Enqueue(db.Database.GetDbConnection());
            return db;
        }

        public async Task<BlokeBotDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default
        )
        {
            var db = await inner.CreateDbContextAsync(cancellationToken);
            _connections.Enqueue(db.Database.GetDbConnection());
            return db;
        }
    }

    private sealed class InterceptedSqliteBlokeBotDbFactory(
        SqliteConnection keeperConnection,
        DbContextOptions<BlokeBotDbContext> options
    ) : IDbContextFactory<BlokeBotDbContext>, IAsyncDisposable
    {
        public static async Task<InterceptedSqliteBlokeBotDbFactory> CreateAsync(
            IInterceptor interceptor
        )
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = $"BlokeBotInterceptedTests-{Guid.NewGuid():N}",
                Mode = SqliteOpenMode.Memory,
                Cache = SqliteCacheMode.Shared,
                Pooling = false,
                DefaultTimeout = 0,
            }.ToString();
            var keeperConnection = new SqliteConnection(connectionString);
            await keeperConnection.OpenAsync();
            var creationOptions = new DbContextOptionsBuilder<BlokeBotDbContext>()
                .UseSqlite(connectionString)
                .Options;
            await using (var db = new BlokeBotDbContext(creationOptions))
            {
                await db.Database.EnsureCreatedAsync();
            }

            var options = new DbContextOptionsBuilder<BlokeBotDbContext>()
                .UseSqlite(connectionString)
                .AddInterceptors(interceptor)
                .Options;
            return new InterceptedSqliteBlokeBotDbFactory(keeperConnection, options);
        }

        public BlokeBotDbContext CreateDbContext()
        {
            return new(options);
        }

        public Task<BlokeBotDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default
        )
        {
            return Task.FromResult(CreateDbContext());
        }

        public async ValueTask DisposeAsync()
        {
            await keeperConnection.DisposeAsync();
        }
    }

    private sealed class CommitCancellationInterceptor : DbTransactionInterceptor
    {
        private bool _failNextCommit;

        public int CommitAttempts { get; private set; }

        public CancellationToken ObservedCancellationToken { get; private set; }

        public void FailNextCommit()
        {
            _failNextCommit = true;
        }

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default
        )
        {
            if (!_failNextCommit)
            {
                return ValueTask.FromResult(result);
            }

            _failNextCommit = false;
            CommitAttempts++;
            ObservedCancellationToken = cancellationToken;
            return ValueTask.FromException<InterceptionResult>(
                new OperationCanceledException("commit cancellation")
            );
        }
    }

    private sealed class TestDatabaseException : DbException;

    private sealed class ThrowingGiveawayChangeNotification(string failureMessage)
        : IPointsGiveawayChangeNotification
    {
        public int Attempts { get; private set; }

        public ValueTask NotifyAsync(CancellationToken cancellationToken)
        {
            Attempts++;
            return ValueTask.FromException(new IOException(failureMessage));
        }
    }

    private sealed class RecordingSchedulerNotification : IPointsGiveawaySchedulerNotification
    {
        public List<string> Messages { get; } = [];

        public ValueTask SendAsync(
            PointsGiveawaySchedule schedule,
            string message,
            CancellationToken cancellationToken
        )
        {
            Messages.Add(message);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ScriptedPublicChatSender(PublicChatSendOutcome outcome)
        : IPublicChatMessageSender
    {
        internal List<string> Messages { get; } = [];

        public ValueTask<PublicChatSendOutcome> SendAsync(
            string channel,
            string message,
            PublicChatDeliveryDeadline deadline,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            Messages.Add(message);
            return ValueTask.FromResult(outcome);
        }
    }

    private sealed class ThrowingSchedulerNotification(string failureMessage)
        : IPointsGiveawaySchedulerNotification
    {
        public ValueTask SendAsync(
            PointsGiveawaySchedule schedule,
            string message,
            CancellationToken cancellationToken
        )
        {
            return ValueTask.FromException(new HttpRequestException(failureMessage));
        }
    }

    private class StaticTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        protected DateTimeOffset UtcNow { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow()
        {
            return UtcNow;
        }

        public override long GetTimestamp()
        {
            return UtcNow.UtcTicks;
        }

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }

    private sealed class AutoAdvanceTimeProvider(DateTimeOffset utcNow) : StaticTimeProvider(utcNow)
    {
        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period
        )
        {
            if (dueTime > TimeSpan.Zero)
            {
                UtcNow = UtcNow.Add(dueTime);
            }

            callback(state);
            return CompletedTimer.Instance;
        }
    }

    private sealed class CompletedTimer : ITimer
    {
        internal static CompletedTimer Instance { get; } = new();

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            return false;
        }

        public void Dispose() { }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingGiveawayScheduler : IPointsGiveawayScheduler
    {
        public List<int> Cancelled { get; } = [];

        public void Schedule(PointsGiveawaySchedule schedule) { }

        public void Cancel(int giveawayId)
        {
            Cancelled.Add(giveawayId);
        }
    }

    private sealed class FixedPointsRandom : IPointsRandom
    {
        public double NextDouble()
        {
            return 0;
        }

        public int Next(int minValue, int maxValue)
        {
            return minValue;
        }
    }

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        private readonly Handler _handler = new();

        public HttpClient CreateClient(string name)
        {
            return new(_handler, disposeHandler: false);
        }

        private sealed class Handler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken
            )
            {
                return Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            """{"data":[]}""",
                            Encoding.UTF8,
                            "application/json"
                        ),
                    }
                );
            }
        }
    }

    private sealed class StaticHostBotAppAccessTokenSource : IHostBotAppAccessTokenSource
    {
        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult("app-token");
        }
    }

    private sealed class ThrowingHostBotAppAccessTokenSource(Exception failure)
        : IHostBotAppAccessTokenSource
    {
        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
        {
            throw failure;
        }
    }

    private sealed class UnavailableHostBotAccountTokenStatusProvider
        : IHostBotAccountTokenStatusProvider
    {
        public Task<ActiveBotAccountTokenStatus> GetActiveTokenStatusAsync(
            string channelLogin,
            IEnumerable<string?> requiredScopes,
            CancellationToken cancellationToken
        )
        {
            return Task.FromResult(
                new ActiveBotAccountTokenStatus
                {
                    BotLogin = string.Empty,
                    Status = new TokenStatus.Unavailable(
                        AccessTokenUnavailableReason.MissingRefreshToken,
                        []
                    ),
                }
            );
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
        {
            return NullLoggerScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

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
