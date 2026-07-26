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

public abstract partial class PointsGiveawaySchedulerTestBase
{
    private protected static PointsGiveawayScheduler CreateScheduler(
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

    private protected static PointOperationOutcome.Succeeded Successful(
        PointOperationOutcome outcome
    )
    {
        return outcome.Match(
            succeeded => succeeded,
            _ => throw new InvalidOperationException("Expected a successful giveaway reply.")
        );
    }

    private protected static PointOperationOutcome.Failed Failed(PointOperationOutcome outcome)
    {
        return outcome.Match(
            _ => throw new InvalidOperationException("Expected a failed giveaway reply."),
            failed => failed
        );
    }

    private protected static PointsGiveawayScheduler CreateScheduler(
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

    private protected static PointsGiveawayService CreateGiveawayService(
        SqliteBlokeBotDbFactory dbFactory,
        IPointsGiveawayScheduler scheduler,
        IHostBotAppAccessTokenSource? appTokens = null,
        bool streamIsLive = false
    )
    {
        var httpClientFactory = new FakeHttpClientFactory(streamIsLive);
        var options = BotSettings.FromOptions(new BotOptions());
        var helix = new HelixClient(
            httpClientFactory,
            global::BlokeBot.Twitch.TwitchEndpointPolicy.Default
        );
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

    private protected static PointsGiveawayDrawService CreateDrawService(
        IDbContextFactory<BlokeBotDbContext> dbFactory
    )
    {
        return new(dbFactory, new PointBalanceService(dbFactory), new FixedPointsRandom());
    }

    private protected static PointsGiveawaySchedule ScheduleEndingAfter(DateTimeOffset now)
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

    private protected static Result<
        TValue,
        PointsGiveawaySchedulerTransientFailure
    > Failure<TValue>()
    {
        return Result<TValue, PointsGiveawaySchedulerTransientFailure>.Error(
            new PointsGiveawaySchedulerTransientFailure(
                new SqliteException("database busy", SQLitePCL.raw.SQLITE_BUSY)
            )
        );
    }

    private protected static async Task<int> SeedHostAsync(
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

    private protected static async Task SeedSettingsAsync(
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

    private protected static async Task<int> SeedGiveawayAsync(
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
}
