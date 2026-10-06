using System.Globalization;
using System.Numerics;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class PointOwnerConcurrencyTests
{
    [Test]
    public async Task Sqlite_FileBackedMixedNativeOwnersPreserveEveryDeltaAndDelete() =>
        await MixedOwnersAsync(await PointProviderFixture.SqliteFileAsync());

    [Test, Explicit]
    public async Task PostgreSql_MixedNativeOwnersPreserveEveryDeltaAndDelete() =>
        await MixedOwnersAsync(await PointProviderFixture.PostgreSqlAsync());

    private static async Task MixedOwnersAsync(PointProviderFixture database)
    {
        await using var owned = database;
        var hostId = await database.SeedHostAsync();
        var service = new PointBalanceService(database);
        (await service.Add(hostId, "viewer", new(100), "streamer", "seed").ExecuteAsync(default))
            .Match(_ => true, _ => false)
            .ShouldBeTrue();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var operations = new[]
        {
            service.Add(hostId, "viewer", new(7), "streamer", "concurrent"),
            service.Remove(hostId, "viewer", new(30), "streamer", "concurrent"),
            service.Transfer(hostId, "viewer", "recipient", new(20)),
            service.ApplyGamble(hostId, "viewer", new(10), new PointGambleOutcome.Won()),
        };
        var pending = operations
            .Select(async operation =>
            {
                await start.Task;
                (await operation.ExecuteAsync(default)).Match(_ => true, _ => false).ShouldBeTrue();
            })
            .ToArray();
        start.SetResult();
        await Task.WhenAll(pending);
        await using (var verify = database.CreateDbContext())
        {
            (
                await verify.PointBalances.SingleAsync(value => value.Login == "viewer")
            ).Amount.ShouldBe("67");
            (
                await verify.PointBalances.SingleAsync(value => value.Login == "recipient")
            ).Amount.ShouldBe("20");
            var entries = await verify
                .PointLedgerEntries.Where(value => value.Login == "viewer")
                .OrderBy(value => value.Id)
                .ToArrayAsync();
            var running = BigInteger.Zero;
            foreach (var entry in entries)
            {
                running += BigInteger.Parse(entry.Delta, CultureInfo.InvariantCulture);
                entry.BalanceAfter.ShouldBe(running.ToString(CultureInfo.InvariantCulture));
            }
            running.ShouldBe(new BigInteger(67));
            (await verify.PointLedgerEntries.CountAsync()).ShouldBe(6);
        }
        var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var add = Task.Run(async () =>
        {
            await second.Task;
            (
                await service
                    .Add(hostId, "viewer", new(11), "streamer", "delete-race")
                    .ExecuteAsync(default)
            )
                .Match(_ => true, _ => false)
                .ShouldBeTrue();
        });
        var delete = Task.Run(async () =>
        {
            await second.Task;
            (
                await service
                    .DeleteBalance(hostId, "viewer", "streamer", "delete-race")
                    .ExecuteAsync(default)
            )
                .Match(_ => true, _ => false)
                .ShouldBeTrue();
        });
        second.SetResult();
        await Task.WhenAll(add, delete);
        await using var final = database.CreateDbContext();
        var ledger = await final
            .PointLedgerEntries.Where(value => value.Login == "viewer")
            .OrderBy(value => value.Id)
            .ToArrayAsync();
        var balance = ledger.Aggregate(
            BigInteger.Zero,
            (sum, entry) => sum + BigInteger.Parse(entry.Delta, CultureInfo.InvariantCulture)
        );
        var row = await final.PointBalances.SingleOrDefaultAsync(value => value.Login == "viewer");
        (row?.Amount ?? "0").ShouldBe(balance.ToString(CultureInfo.InvariantCulture));
        (balance == 0 || balance == 11).ShouldBeTrue();
        ledger.Count(value => value.Kind == PointLedgerKind.DeleteBalance).ShouldBe(1);
        (
            await final.PointBalances.SingleAsync(value => value.Login == "recipient")
        ).Amount.ShouldBe("20");
    }
}
