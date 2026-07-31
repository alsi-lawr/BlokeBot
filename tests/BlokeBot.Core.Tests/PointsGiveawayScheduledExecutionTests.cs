using System.Collections.Concurrent;
using System.Data.Common;
using System.Net;
using System.Text;
using System.Text.Json;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Core.Features.Points;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Core.Features.Points.Configuration;
using BlokeBot.Core.Features.Points.Gambling;
using BlokeBot.Core.Features.Points.Giveaways;
using BlokeBot.Core.Features.Replies;
using BlokeBot.Eventing;
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

namespace BlokeBot.Core.Tests;

public sealed class PointsGiveawayScheduledExecutionTests : PointsGiveawaySchedulerTestBase
{
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
        thrown.IntendedOutcome.ShouldBeOfType<PointsGiveawayDrawOutcome.Winners>();
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
        operations.NotifiedHostIds.ShouldBe([7]);
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
}
