using System.Globalization;
using System.Numerics;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Core.Features.Points.Giveaways;
using BlokeBot.Core.Features.RequestBoards;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class PointWatchOrderTests : PointsTestBase
{
    public enum OwnerOperation
    {
        Add,
        Remove,
        Delete,
        Transfer,
        Gamble,
        Reward,
        Reservation,
        Refund,
        BoardConfiguration,
    }

    [Test]
    [Arguments(OwnerOperation.Add, true), Arguments(OwnerOperation.Add, false)]
    [Arguments(OwnerOperation.Remove, true), Arguments(OwnerOperation.Remove, false)]
    [Arguments(OwnerOperation.Delete, true), Arguments(OwnerOperation.Delete, false)]
    [Arguments(OwnerOperation.Transfer, true), Arguments(OwnerOperation.Transfer, false)]
    [Arguments(OwnerOperation.Gamble, true), Arguments(OwnerOperation.Gamble, false)]
    [Arguments(OwnerOperation.Reward, true), Arguments(OwnerOperation.Reward, false)]
    [Arguments(OwnerOperation.Reservation, true), Arguments(OwnerOperation.Reservation, false)]
    [Arguments(OwnerOperation.Refund, true), Arguments(OwnerOperation.Refund, false)]
    [
        Arguments(OwnerOperation.BoardConfiguration, true),
        Arguments(OwnerOperation.BoardConfiguration, false)
    ]
    public async Task Sqlite_WatchAndRealOwnerForcedOrdersPreserveLedger(
        OwnerOperation operation,
        bool watchFirst
    )
    {
        var barrier = new PointTransactionBarrier();
        await OrderedAsync(
            await PointProviderFixture.SqliteFileAsync(barrier),
            barrier,
            operation,
            watchFirst
        );
    }

    [Test, Explicit]
    [Arguments(OwnerOperation.Add, true), Arguments(OwnerOperation.Add, false)]
    [Arguments(OwnerOperation.Remove, true), Arguments(OwnerOperation.Remove, false)]
    [Arguments(OwnerOperation.Delete, true), Arguments(OwnerOperation.Delete, false)]
    [Arguments(OwnerOperation.Transfer, true), Arguments(OwnerOperation.Transfer, false)]
    [Arguments(OwnerOperation.Gamble, true), Arguments(OwnerOperation.Gamble, false)]
    [Arguments(OwnerOperation.Reward, true), Arguments(OwnerOperation.Reward, false)]
    [Arguments(OwnerOperation.Reservation, true), Arguments(OwnerOperation.Reservation, false)]
    [Arguments(OwnerOperation.Refund, true), Arguments(OwnerOperation.Refund, false)]
    [
        Arguments(OwnerOperation.BoardConfiguration, true),
        Arguments(OwnerOperation.BoardConfiguration, false)
    ]
    public async Task PostgreSql_WatchAndRealOwnerForcedOrdersPreserveLedger(
        OwnerOperation operation,
        bool watchFirst
    )
    {
        var barrier = new PointTransactionBarrier();
        await OrderedAsync(
            await PointProviderFixture.PostgreSqlAsync(barrier),
            barrier,
            operation,
            watchFirst
        );
    }

    private static async Task OrderedAsync(
        PointProviderFixture database,
        PointTransactionBarrier barrier,
        OwnerOperation operation,
        bool watchFirst
    )
    {
        await using var owned = database;
        await using var fixture = new WatchTimeTestSupport(database);
        var hostId = await fixture.SeedAsync();
        _ = await fixture.ConnectAsync(hostId);
        await using (var seed = database.CreateDbContext())
        {
            (await seed.Hosts.SingleAsync()).EnabledFeatures = HostFeatureFlags.All;
            _ = await seed.SaveChangesAsync();
        }
        fixture.Http.Response = (_, _) =>
            Task.FromResult(
                WatchHttp.Json(
                    """{"data":[{"user_id":"300","user_login":"viewer","user_name":"Viewer"}],"pagination":{}}"""
                )
            );
        await fixture.Runtime.ReconcileAsync(default);
        var balances = new PointBalanceService(database);
        _ = Mutation(
            await balances.Add(hostId, "viewer", new(100), "streamer", "seed").ExecuteAsync(default)
        );
        var boards = new RequestBoardService(database, fixture.Events, fixture.Clock);
        PublicRequestSubmissionView? reserved = null;
        if (
            operation
            is OwnerOperation.Reservation
                or OwnerOperation.Refund
                or OwnerOperation.BoardConfiguration
        )
        {
            var cost = operation == OwnerOperation.BoardConfiguration ? "1" : "25";
            _ = (
                await boards.ConfigureAsync(hostId, Board(cost), default)
            ).ShouldBeOfType<RequestBoardResult<RequestBoardSummary>.Succeeded>();
            if (operation is OwnerOperation.Refund or OwnerOperation.BoardConfiguration)
            {
                reserved = (await boards.SubmitAsync(hostId, "games", Submission(), default))
                    .ShouldBeOfType<RequestBoardResult<PublicRequestSubmissionView>.Succeeded>()
                    .Value;
            }
        }
        var giveawayId = 0;
        if (operation == OwnerOperation.Reward)
        {
            await using var seed = database.CreateDbContext();
            var giveaway = new PointsGiveaway
            {
                HostId = hostId,
                StartedAtUtc = fixture.Clock.GetUtcNow().UtcDateTime,
                EndsAtUtc = fixture.Clock.GetUtcNow().AddMinutes(5).UtcDateTime,
                MinimumPayout = "10",
                MaximumPayout = "10",
                Entrants =
                [
                    new() { Login = "viewer", JoinedAtUtc = fixture.Clock.GetUtcNow().UtcDateTime },
                ],
            };
            _ = seed.PointsGiveaways.Add(giveaway);
            _ = await seed.SaveChangesAsync();
            giveawayId = giveaway.Id;
        }
        int baseline;
        await using (var read = database.CreateDbContext())
        {
            baseline = await read.PointLedgerEntries.CountAsync();
        }
        ((ManualTestTimeProvider)fixture.Clock).Advance(TimeSpan.FromMinutes(5));
        async Task OwnerAsync()
        {
            switch (operation)
            {
                case OwnerOperation.Add:
                    _ = Mutation(
                        await balances
                            .Add(hostId, "viewer", new(11), "streamer", "ordered")
                            .ExecuteAsync(default)
                    );
                    break;
                case OwnerOperation.Remove:
                    _ = Mutation(
                        await balances
                            .Remove(hostId, "viewer", new(30), "streamer", "ordered")
                            .ExecuteAsync(default)
                    );
                    break;
                case OwnerOperation.Delete:
                    _ = Mutation(
                        await balances
                            .DeleteBalance(hostId, "viewer", "streamer", "ordered")
                            .ExecuteAsync(default)
                    );
                    break;
                case OwnerOperation.Transfer:
                    _ = Mutation(
                        await balances
                            .Transfer(hostId, "viewer", "recipient", new(20))
                            .ExecuteAsync(default)
                    );
                    break;
                case OwnerOperation.Gamble:
                    _ = Mutation(
                        await balances
                            .ApplyGamble(hostId, "viewer", new(10), new PointGambleOutcome.Won())
                            .ExecuteAsync(default)
                    );
                    break;
                case OwnerOperation.Reward:
                    _ = (
                        await new PointsGiveawayDrawService(
                            database,
                            balances,
                            new FixedPointsRandom()
                        ).DrawOutcomeAsync(giveawayId, default)
                    ).ShouldBeOfType<PointsGiveawayDrawOutcome.Winners>();
                    break;
                case OwnerOperation.Reservation:
                    _ = (
                        await boards.SubmitAsync(hostId, "games", Submission(), default)
                    ).ShouldBeOfType<RequestBoardResult<PublicRequestSubmissionView>.Succeeded>();
                    break;
                case OwnerOperation.Refund:
                    _ = (
                        await boards.WithdrawAsync(
                            hostId,
                            reserved!.Id,
                            RequestBoardTestActor.ForLogin("viewer"),
                            default
                        )
                    ).ShouldBeOfType<RequestBoardResult<PublicRequestSubmissionView>.Succeeded>();
                    break;
                case OwnerOperation.BoardConfiguration:
                    _ = (
                        await boards.ConfigureAsync(
                            hostId,
                            Board(PointAmount.MaximumValue.ToString(CultureInfo.InvariantCulture)),
                            default
                        )
                    ).ShouldBeOfType<RequestBoardResult<RequestBoardSummary>.Succeeded>();
                    break;
            }
        }
        barrier.Arm();
        var blocked = watchFirst ? OwnerAsync() : fixture.Runtime.ProcessDueAsync(default);
        await barrier.Entered.WaitAsync(TimeSpan.FromSeconds(15));
        try
        {
            if (watchFirst)
            {
                await fixture.Runtime.ProcessDueAsync(default);
            }
            else
            {
                await OwnerAsync();
            }
        }
        finally
        {
            barrier.Release();
        }
        await blocked;
        await fixture.Runtime.ProcessDueAsync(default);
        var expected = operation switch
        {
            OwnerOperation.Add => 118,
            OwnerOperation.Remove => 77,
            OwnerOperation.Delete => watchFirst ? 0 : 7,
            OwnerOperation.Transfer => 87,
            OwnerOperation.Gamble or OwnerOperation.Reward => 117,
            OwnerOperation.Reservation => 82,
            OwnerOperation.Refund => 107,
            OwnerOperation.BoardConfiguration => watchFirst ? 106 : 99,
        };
        (await balances.GetBalanceAsync(hostId, "viewer", default)).Balance.ShouldBe(
            new PointAmount(expected)
        );
        await using var verify = database.CreateDbContext();
        var ledger = await verify
            .PointLedgerEntries.Where(value => value.Login == "viewer")
            .OrderBy(value => value.Id)
            .ToArrayAsync();
        var running = BigInteger.Zero;
        foreach (var entry in ledger)
        {
            running += BigInteger.Parse(entry.Delta, CultureInfo.InvariantCulture);
            entry.BalanceAfter.ShouldBe(running.ToString(CultureInfo.InvariantCulture));
        }
        running.ShouldBe(new BigInteger(expected));
        var expectedWatch = operation == OwnerOperation.BoardConfiguration && !watchFirst ? 0 : 1;
        ledger
            .Count(value => value.Kind == PointLedgerKind.WatchTimeReward)
            .ShouldBe(expectedWatch);
        if (operation != OwnerOperation.BoardConfiguration)
        {
            var committed = ledger.Skip(baseline).ToArray();
            committed.Length.ShouldBe(2);
            (committed[0].Kind == PointLedgerKind.WatchTimeReward).ShouldBe(watchFirst);
        }
        if (operation == OwnerOperation.Transfer)
        {
            (await balances.GetBalanceAsync(hostId, "recipient", default)).Balance.ShouldBe(
                new PointAmount(20)
            );
        }
    }

    private static ConfigureRequestBoardCommand Board(string cost) =>
        new(
            "games",
            "Games",
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

    private static SubmitRequestCommand Submission() =>
        new(
            Guid.NewGuid(),
            RequestBoardTestActor.ForLogin("viewer"),
            "Request",
            "",
            [],
            new Dictionary<string, string>()
        );
}
