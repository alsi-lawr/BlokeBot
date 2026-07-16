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

public sealed class PointsGiveawayDrawTests : PointsGiveawaySchedulerTestBase
{
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

        outcome.ShouldBeOfType<PointsGiveawayJoinOutcome.DuplicateJoin>();
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

        outcome.ShouldBeOfType<PointsGiveawayJoinOutcome.NotEligible>();
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

        outcome.ShouldBeOfType<PointsGiveawayDrawOutcome.NoEntrants>();
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

        var winners = outcome.ShouldBeOfType<PointsGiveawayDrawOutcome.Winners>().Payouts;
        winners.Single().Login.ShouldBe("entrant");
        winners.Single().Payout.ShouldBe(PointAmount.ParseAbsolute("10"));
    }

    [Test]
    public async Task MultipleWinnersWithCappedBalance_DrawingRollsBackEntirePayout()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var giveawayId = await SeedGiveawayAsync(
            dbFactory,
            hostId,
            DateTime.UtcNow,
            DateTime.UtcNow.AddMinutes(5),
            "first"
        );
        await SeedSettingsAsync(dbFactory, hostId);
        await using (var seed = await dbFactory.CreateDbContextAsync())
        {
            var giveaway = await seed.PointsGiveaways.SingleAsync(x => x.Id == giveawayId);
            giveaway.WinnerCount = 2;
            giveaway.Entrants.Add(
                new PointsGiveawayEntrant { Login = "capped", JoinedAtUtc = DateTime.UtcNow }
            );
            seed.PointBalances.Add(
                new PointBalance
                {
                    HostId = hostId,
                    Login = "capped",
                    Amount = PointAmount.MaximumValue.ToString(),
                    UpdatedAtUtc = DateTime.UtcNow,
                }
            );
            await seed.SaveChangesAsync();
        }
        var service = CreateGiveawayService(dbFactory, new RecordingGiveawayScheduler());

        var outcome = await service.DrawOutcomeAsync(giveawayId, CancellationToken.None);

        var failure = outcome.ShouldBeOfType<PointsGiveawayDrawOutcome.PayoutFailed>();
        failure.Failure.ShouldBeOfType<PointBalanceMutationFailure.CapExceeded>();
        var reply = Failed(
            new PointsGiveawayMessageFormatter().Reply(outcome, new ReplyDeliveryMap())
        );
        reply.Message.ShouldBe("Giveaway prizes could not be awarded.");
        reply.Message.ShouldNotContain("Giveaway winners");
        await using var db = await dbFactory.CreateDbContextAsync();
        var persisted = await db.PointsGiveaways.SingleAsync(x => x.Id == giveawayId);
        persisted.Status.ShouldBe(PointsGiveawayStatus.Active);
        persisted.CompletedAtUtc.ShouldBeNull();
        (await db.PointsGiveawayWinners.CountAsync(x => x.GiveawayId == giveawayId)).ShouldBe(0);
        (await db.PointLedgerEntries.CountAsync(x => x.GiveawayId == giveawayId)).ShouldBe(0);
        var balances = await db.PointBalances.OrderBy(x => x.Login).ToListAsync();
        balances
            .Select(balance => (balance.Login, balance.Amount))
            .ShouldBe([("capped", PointAmount.MaximumValue.ToString())]);
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

        first.ShouldBeOfType<PointsGiveawayDrawOutcome.Winners>();
        second.ShouldBeOfType<PointsGiveawayDrawOutcome.NotActive>();
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
