using System.Globalization;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Core.Features.Points.Commands;
using BlokeBot.Core.Features.RequestBoards;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class PointMutationCompatibilityTests : PointsTestBase
{
    [Test]
    public async Task Sqlite_MutableBoardExposurePreservesManualFailureAndCallerReply()
    {
        var barrier = new PointTransactionBarrier();
        await CompatibilityAsync(await PointProviderFixture.SqliteFileAsync(barrier), barrier);
    }

    [Test, Explicit]
    public async Task PostgreSql_MutableBoardExposurePreservesManualFailureAndCallerReply()
    {
        var barrier = new PointTransactionBarrier();
        await CompatibilityAsync(await PointProviderFixture.PostgreSqlAsync(barrier), barrier);
    }

    private static async Task CompatibilityAsync(
        PointProviderFixture database,
        PointTransactionBarrier barrier
    )
    {
        await using var owned = database;
        var hostId = await database.SeedHostAsync();
        await using (var seed = database.CreateDbContext())
        {
            (await seed.Hosts.SingleAsync()).EnabledFeatures = HostFeatureFlags.All;
            var settings = await seed.PointsSettings.SingleAsync();
            settings.InsufficientBalanceReply = "configured insufficient";
            settings.InvalidAmountReply = "configured invalid";
            settings.GamblingWinRatePercent = 100;
            _ = await seed.SaveChangesAsync();
        }
        var balances = new PointBalanceService(database);
        _ = Mutation(
            await balances.Add(hostId, "viewer", new(100), "streamer", "seed").ExecuteAsync(default)
        );
        _ = Mutation(
            await balances
                .Add(hostId, "a", new(PointAmount.MaximumValue), "streamer", "seed")
                .ExecuteAsync(default)
        );
        var boards = new RequestBoardService(
            database,
            TestEventBus.Create<AppEventKind>(),
            TimeProvider.System
        );
        foreach (var slug in new[] { "one", "two" })
        {
            _ = (
                await boards.ConfigureAsync(hostId, Board(slug, "1"), default)
            ).ShouldBeOfType<RequestBoardResult<RequestBoardSummary>.Succeeded>();
            _ = (
                await boards.SubmitAsync(
                    hostId,
                    slug,
                    new(
                        Guid.NewGuid(),
                        RequestBoardTestActor.ForLogin("viewer"),
                        slug,
                        "",
                        [],
                        new Dictionary<string, string>()
                    ),
                    default
                )
            ).ShouldBeOfType<RequestBoardResult<PublicRequestSubmissionView>.Succeeded>();
            _ = (
                await boards.ConfigureAsync(
                    hostId,
                    Board(slug, PointAmount.MaximumValue.ToString(CultureInfo.InvariantCulture)),
                    default
                )
            ).ShouldBeOfType<RequestBoardResult<RequestBoardSummary>.Succeeded>();
        }
        var before = await LedgerCountAsync(database);
        var add = (
            await balances
                .Add(hostId, "viewer", new(1), "streamer", "rejected")
                .ExecuteAsync(default)
        )
            .Match(
                _ => throw new InvalidOperationException("Expected capacity rejection"),
                failure => failure
            )
            .ShouldBeOfType<PointBalanceMutationFailure.CapExceeded>();
        add.Balance.ShouldBe(new PointAmount(98));
        var gamble = (
            await balances
                .ApplyGamble(hostId, "viewer", new(1), new PointGambleOutcome.Won())
                .ExecuteAsync(default)
        )
            .Match(
                _ => throw new InvalidOperationException("Expected capacity rejection"),
                failure => failure
            )
            .ShouldBeOfType<PointBalanceMutationFailure.CapExceeded>();
        gamble.Balance.ShouldBe(new PointAmount(98));
        var insufficient = (
            await balances
                .ApplyGamble(hostId, "viewer", new(99), new PointGambleOutcome.Won())
                .ExecuteAsync(default)
        )
            .Match(
                _ => throw new InvalidOperationException("Expected insufficient rejection"),
                failure => failure
            )
            .ShouldBeOfType<PointBalanceMutationFailure.InsufficientBalance>();
        insufficient.Balance.ShouldBe(new PointAmount(98));
        (await LedgerCountAsync(database)).ShouldBe(before);
        List<string> replies = [];
        var give = new GivePointsCommandStrategy(
            new(database),
            balances,
            new FixedPointTargetUserLookup(["a"])
        );
        await give.ExecuteAsync(
            CommandContext(
                hostId,
                "z",
                "streamer",
                "givepoints",
                ["a", "1"],
                replies,
                PointsCommandKind.GivePoints
            ),
            default
        );
        replies.ShouldBe(["configured insufficient"]);
        var strategy = new GambleCommandStrategy(
            new(database),
            balances,
            new FixedPointsRandom(),
            new(TimeProvider.System),
            Options.Create(new BlokeBotOptions())
        );
        barrier.Arm();
        var pending = strategy
            .ExecuteAsync(
                CommandContext(
                    hostId,
                    "viewer",
                    "streamer",
                    "gamble",
                    ["10"],
                    replies,
                    PointsCommandKind.Gamble
                ),
                default
            )
            .AsTask();
        await barrier.Entered.WaitAsync(TimeSpan.FromSeconds(15));
        _ = Mutation(
            await balances
                .Remove(hostId, "viewer", new(90), "streamer", "concurrent debit")
                .ExecuteAsync(default)
        );
        barrier.Release();
        await pending;
        replies.ShouldBe(["configured insufficient", "configured insufficient"]);
        (await LedgerCountAsync(database)).ShouldBe(before + 1);
        (await balances.GetBalanceAsync(hostId, "viewer", default)).Balance.ShouldBe(
            new PointAmount(8)
        );
    }

    private static ConfigureRequestBoardCommand Board(string slug, string cost) =>
        new(
            slug,
            slug,
            "",
            true,
            cost,
            RequestBoardRefundPolicy.RejectedOrWithdrawn,
            3,
            0,
            10,
            true,
            [new("details", "Details", RequestBoardFieldKind.Text, false, 500)]
        );

    private static async Task<int> LedgerCountAsync(PointProviderFixture database)
    {
        await using var db = database.CreateDbContext();
        return await db.PointLedgerEntries.CountAsync();
    }
}
