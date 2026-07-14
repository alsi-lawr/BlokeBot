using BlokeBot.Features.Points.Balances;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Tests;

public sealed class PointBalanceIOTests
{
    [Test]
    public async Task BalanceMutation_Creating_RemainsLazyUntilExecution()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory);
        var operation = new PointBalanceService(dbFactory).Add(
            hostId,
            "viewer",
            PointAmount.ParseAbsolute("10"),
            "streamer",
            "test"
        );

        await using (var before = await dbFactory.CreateDbContextAsync())
        {
            (await before.PointBalances.CountAsync()).ShouldBe(0);
        }

        var result = await operation.ExecuteAsync(CancellationToken.None);

        result.Match(static _ => true, static _ => false).ShouldBeTrue();
        await using var after = await dbFactory.CreateDbContextAsync();
        (await after.PointBalances.SingleAsync()).Amount.ShouldBe("10");
    }

    [Test]
    public async Task CancelledBalanceMutation_Executing_PropagatesWithoutPersistence()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory);
        var operation = new PointBalanceService(dbFactory).Add(
            hostId,
            "viewer",
            PointAmount.ParseAbsolute("10"),
            "streamer",
            "test"
        );
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            operation.ExecuteAsync(cancellation.Token).AsTask()
        );

        await using var db = await dbFactory.CreateDbContextAsync();
        (await db.PointBalances.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task UnexpectedPersistenceFault_Executing_PropagatesUnchanged()
    {
        var expected = new InvalidOperationException("unexpected persistence fault");
        var operation = new PointBalanceService(new ThrowingDbContextFactory(expected)).Add(
            1,
            "viewer",
            PointAmount.ParseAbsolute("10"),
            "streamer",
            "test"
        );

        var thrown = await Should.ThrowAsync<InvalidOperationException>(() =>
            operation.ExecuteAsync(CancellationToken.None).AsTask()
        );

        thrown.ShouldBeSameAs(expected);
    }

    private static async Task<int> SeedHostAsync(SqliteBlokeBotDbFactory dbFactory)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var host = new BotHost
        {
            Login = "streamer",
            DisplayName = "Streamer",
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.Hosts.Add(host);
        await db.SaveChangesAsync();
        return host.Id;
    }

    private sealed class ThrowingDbContextFactory(Exception exception)
        : IDbContextFactory<BlokeBotDbContext>
    {
        public BlokeBotDbContext CreateDbContext()
        {
            throw exception;
        }

        public Task<BlokeBotDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default
        )
        {
            return Task.FromException<BlokeBotDbContext>(exception);
        }
    }
}
