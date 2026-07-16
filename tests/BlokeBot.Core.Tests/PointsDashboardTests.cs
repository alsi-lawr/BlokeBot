using System.Numerics;
using BlokeBot.Commands;
using BlokeBot.Core.Features.Commands;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Core.Features.Points;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Core.Features.Points.Commands;
using BlokeBot.Core.Features.Points.Configuration;
using BlokeBot.Core.Features.Points.Dashboard;
using BlokeBot.Core.Features.Points.Gambling;
using BlokeBot.Core.Features.Points.Replies;
using BlokeBot.Core.Features.Replies;
using BlokeBot.Eventing;
using BlokeBot.Functional;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Core.Tests;

public sealed class PointsDashboardTests : PointsTestBase
{
    [Test]
    public async Task UnknownDashboardTarget_AddingPoints_ReturnsTypedFailureWithoutBalance()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var service = new PointsDashboardService(
            new PointBalanceService(dbFactory),
            null!,
            new PointsChangeNotifier(TestEventBus.Create<AppEventKind>()),
            new FixedPointTargetUserLookup([])
        );

        var result = await service.AddAsync(
            hostId,
            "@missingviewer",
            "10",
            "streamer",
            CancellationToken.None
        );

        await using var db = await dbFactory.CreateDbContextAsync();
        Failure(result).Message.ShouldBe("Twitch user @missingviewer was not found.");
        (await db.PointBalances.CountAsync(CancellationToken.None)).ShouldBe(0);
    }

    [Test]
    public async Task MentionPrefixedDashboardTarget_AddingPoints_StoresNormalizedLogin()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var service = new PointsDashboardService(
            new PointBalanceService(dbFactory),
            null!,
            new PointsChangeNotifier(TestEventBus.Create<AppEventKind>()),
            new FixedPointTargetUserLookup(["viewer"])
        );

        var result = await service.AddAsync(
            hostId,
            "@Viewer",
            "10",
            "streamer",
            CancellationToken.None
        );

        await using var db = await dbFactory.CreateDbContextAsync();
        var balance = await db.PointBalances.SingleAsync(CancellationToken.None);
        _ = Success(result);
        balance.Login.ShouldBe("viewer");
        balance.Amount.ShouldBe("10");
    }

    [Test]
    public async Task InvalidFormatOrRange_SubmittingPointAmounts_PreservesInvalidResponses()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var users = new FixedPointTargetUserLookup(["viewer"]);
        var dashboard = new PointsDashboardService(
            new PointBalanceService(dbFactory),
            null!,
            new PointsChangeNotifier(TestEventBus.Create<AppEventKind>()),
            users
        );
        List<string> replies = [];
        var command = new AddPointsCommandStrategy(
            new PointsCommandService(dbFactory),
            new PointBalanceService(dbFactory),
            users
        );

        var dashboardResult = await dashboard.AddAsync(
            hostId,
            "viewer",
            "10.5",
            "streamer",
            CancellationToken.None
        );
        await command.ExecuteAsync(
            CommandContext(
                hostId,
                "moderator",
                "streamer",
                "addpoints",
                ["viewer", (PointAmount.MaximumValue + BigInteger.One).ToString()],
                replies
            ),
            CancellationToken.None
        );

        await using var db = await dbFactory.CreateDbContextAsync();
        Failure(dashboardResult).Message.ShouldBe("Invalid amount.");
        replies.ShouldBe(["That point amount is not valid."]);
        (await db.PointBalances.CountAsync(CancellationToken.None)).ShouldBe(0);
    }

    [Test]
    public async Task LargePointMutation_AddingBalance_PersistsFullPrecisionInBalanceAndLedger()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var balances = new PointBalanceService(dbFactory);
        var amount = PointAmount.ParseAbsolute("123456789012");

        var result = await balances
            .Add(hostId, "viewer", amount, "streamer", "test")
            .ExecuteAsync(CancellationToken.None);

        await using var db = await dbFactory.CreateDbContextAsync();
        var balance = await db.PointBalances.SingleAsync(CancellationToken.None);
        var ledger = await db.PointLedgerEntries.SingleAsync(CancellationToken.None);
        Mutation(result).Balance.ShouldBe(amount);
        balance.Amount.ShouldBe("123456789012");
        ledger.BalanceAfter.ShouldBe("123456789012");
    }

    [Test]
    public async Task ExistingDashboardBalance_Removing_DeletesRowAndWritesAuditLedger()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var balances = new PointBalanceService(dbFactory);
        var service = new PointsDashboardService(
            balances,
            null!,
            new PointsChangeNotifier(TestEventBus.Create<AppEventKind>()),
            new FixedPointTargetUserLookup([])
        );
        _ = await balances
            .Add(hostId, "viewer", PointAmount.ParseAbsolute("25"), "streamer", "test")
            .ExecuteAsync(CancellationToken.None);

        var result = await service.RemoveBalanceAsync(
            hostId,
            "viewer",
            "streamer",
            CancellationToken.None
        );

        await using var db = await dbFactory.CreateDbContextAsync();
        var ledger = await db
            .PointLedgerEntries.OrderBy(x => x.Id)
            .ToListAsync(CancellationToken.None);
        Success(result).Message.ShouldBe("Point balance removed.");
        (await db.PointBalances.CountAsync(CancellationToken.None)).ShouldBe(0);
        ledger.Count.ShouldBe(2);
        ledger[^1].Kind.ShouldBe(PointLedgerKind.DeleteBalance);
        ledger[^1].Login.ShouldBe("viewer");
        ledger[^1].Delta.ShouldBe("-25");
        ledger[^1].BalanceAfter.ShouldBe("0");
        ledger[^1].ActorLogin.ShouldBe("streamer");
        ledger[^1].Note.ShouldBe("dashboard");
    }

    [Test]
    public async Task MissingDashboardBalance_Removing_ReturnsFailureWithoutRows()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var service = new PointsDashboardService(
            new PointBalanceService(dbFactory),
            null!,
            new PointsChangeNotifier(TestEventBus.Create<AppEventKind>()),
            new FixedPointTargetUserLookup([])
        );

        var result = await service.RemoveBalanceAsync(
            hostId,
            "missingviewer",
            "streamer",
            CancellationToken.None
        );

        await using var db = await dbFactory.CreateDbContextAsync();
        Failure(result).Message.ShouldBe("No point balance found.");
        (await db.PointBalances.CountAsync(CancellationToken.None)).ShouldBe(0);
        (await db.PointLedgerEntries.CountAsync(CancellationToken.None)).ShouldBe(0);
    }
}
