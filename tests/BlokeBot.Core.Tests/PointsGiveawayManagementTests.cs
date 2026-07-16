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

public sealed class PointsGiveawayManagementTests : PointsGiveawaySchedulerTestBase
{
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

        _ = Successful(result);
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

        outcome.ShouldBeOfType<PointsGiveawayCancelOutcome.Cancelled>();
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

        _ = Successful(result);
        scheduler.Cancelled.ShouldContain(giveawayId);
        await using var db = await dbFactory.CreateDbContextAsync();
        var giveaway = await db.PointsGiveaways.SingleAsync(x => x.Id == giveawayId);
        giveaway.Status.ShouldBe(PointsGiveawayStatus.Completed);
        giveaway.CompletedAtUtc.ShouldNotBeNull();
    }

    [Test]
    public async Task InvalidSettingsWithActiveGiveaway_RequestingStartOutcome_ReturnsAlreadyActive()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        await SeedSettingsAsync(
            dbFactory,
            hostId,
            settings =>
            {
                settings.GiveawayDurationSeconds = 0;
                settings.GiveawayWinnerCount = 0;
            }
        );
        await SeedGiveawayAsync(dbFactory, hostId, DateTime.UtcNow, DateTime.UtcNow.AddMinutes(5));
        var service = CreateGiveawayService(dbFactory, new RecordingGiveawayScheduler());

        var outcome = await service.StartOutcomeAsync(
            hostId,
            "streamer",
            null,
            CancellationToken.None
        );

        outcome.ShouldBeOfType<PointsGiveawayStartOutcome.AlreadyActive>();
    }

    [Test]
    public async Task DurationBelowOne_RequestingStartOutcome_ReturnsInvalidWithoutStarting()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        await SeedSettingsAsync(
            dbFactory,
            hostId,
            settings => settings.GiveawayDurationSeconds = 0
        );
        var scheduler = new RecordingGiveawayScheduler();
        var service = CreateGiveawayService(dbFactory, scheduler, streamIsLive: true);

        var outcome = await service.StartOutcomeAsync(
            hostId,
            "streamer",
            null,
            CancellationToken.None
        );

        var invalid = outcome.ShouldBeOfType<PointsGiveawayStartOutcome.InvalidConfiguration>();
        invalid.Settings.GiveawayDurationSeconds.ShouldBe(0);
        invalid.Failure.ShouldBeOfType<PointsConfigurationValidationError.GiveawayDurationBelowMinimum>();
        scheduler.Scheduled.ShouldBeEmpty();
        await using var db = await dbFactory.CreateDbContextAsync();
        (await db.PointsGiveaways.CountAsync(x => x.HostId == hostId)).ShouldBe(0);
    }

    [Test]
    public async Task WinnerCountBelowOne_RequestingStartOutcome_ReturnsInvalidWithoutStarting()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        await SeedSettingsAsync(dbFactory, hostId, settings => settings.GiveawayWinnerCount = 0);
        var scheduler = new RecordingGiveawayScheduler();
        var service = CreateGiveawayService(dbFactory, scheduler, streamIsLive: true);

        var outcome = await service.StartOutcomeAsync(
            hostId,
            "streamer",
            null,
            CancellationToken.None
        );

        var invalid = outcome.ShouldBeOfType<PointsGiveawayStartOutcome.InvalidConfiguration>();
        invalid.Settings.GiveawayWinnerCount.ShouldBe(0);
        invalid.Failure.ShouldBeOfType<PointsConfigurationValidationError.GiveawayWinnerCountBelowMinimum>();
        scheduler.Scheduled.ShouldBeEmpty();
        await using var db = await dbFactory.CreateDbContextAsync();
        (await db.PointsGiveaways.CountAsync(x => x.HostId == hostId)).ShouldBe(0);
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

        outcome
            .ShouldBeOfType<PointsGiveawayStartOutcome.Cooldown>()
            .TimeLeft.ShouldBeGreaterThan(TimeSpan.Zero);
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

        outcome.ShouldBeOfType<PointsGiveawayStartOutcome.StreamOffline>();
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

        var unavailable =
            outcome.ShouldBeOfType<PointsGiveawayStartOutcome.StreamLivenessUnavailable>();
        var failure = unavailable.Failure;
        failure.Reason.ShouldBe(HostStreamLivenessUnavailableReason.ProviderRequestFailed);
        failure.FailureType.ShouldBe(typeof(HttpRequestException).FullName);
        failure.Cause.ShouldBeSameAs(expected);
        failure.ToString().ShouldNotContain("provider secret");
        outcome.ToString().ShouldNotContain("provider secret");
        JsonSerializer.Serialize(outcome).ShouldNotContain("provider secret");
        var result = new PointsGiveawayMessageFormatter().Reply(outcome, new ReplyDeliveryMap());
        var failed = Failed(result);
        failed.Message.ShouldBe("Stream status could not be checked right now.");
        failed.Message.ShouldNotBe(unavailable.Settings.StreamOfflineReply);
    }
}
