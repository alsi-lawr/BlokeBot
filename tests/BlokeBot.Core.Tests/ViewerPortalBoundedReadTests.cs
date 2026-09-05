using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Core.Features.ViewerPortal;
using BlokeBot.Persistence.Models;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class ViewerPortalBoundedReadTests
{
    [Test]
    public async Task PointsBudget_PreservesHistoricalParsingAndPrivacyWithoutReturningPartialRanks()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var context = new ViewerPortalTestContext(database);
        var host = await context.HostAsync(
            "alpha",
            HostFeatureFlags.Points | HostFeatureFlags.ViewerPassports
        );
        var service = new PointBalanceService(database);
        await using (var seed = await database.CreateDbContextAsync())
        {
            seed.PointBalances.AddRange(
                Enumerable
                    .Range(0, 10_000)
                    .Select(index => new PointBalance
                    {
                        HostId = host,
                        Login = $"viewer{index:D5}",
                        Amount = index == 0 ? "1K" : "1",
                        UpdatedAtUtc = DateTime.UtcNow,
                    })
            );
            _ = await seed.SaveChangesAsync();
        }
        var nearLimit = await service.GetBoundedLeaderboardAsync(host, publicOnly: true, default);
        nearLimit!.Count.ShouldBe(10_000);
        nearLimit[0].Login.ShouldBe("viewer00000");
        nearLimit[0].Balance.Value.ShouldBe(1000);
        await using (var seed = await database.CreateDbContextAsync())
        {
            _ = seed.PointBalances.Add(
                new PointBalance
                {
                    HostId = host,
                    Login = "hidden",
                    Amount = "9,999",
                    UpdatedAtUtc = DateTime.UtcNow,
                }
            );
            _ = seed.ViewerPassports.Add(
                new ViewerPassport
                {
                    HostId = host,
                    TwitchUserId = "hidden-id",
                    Login = "hidden",
                    Visibility = ViewerPassportVisibility.Private,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow,
                }
            );
            _ = await seed.SaveChangesAsync();
        }
        (await service.GetBoundedLeaderboardAsync(host, publicOnly: true, default))!.Count.ShouldBe(
            10_000
        );
        (await service.GetBoundedLeaderboardAsync(host, publicOnly: false, default)).ShouldBeNull();
        (await service.GetLeaderboardAsync(host, 1, default))[0].Login.ShouldBe("hidden");
        await using (var seed = await database.CreateDbContextAsync())
        {
            _ = seed.PointBalances.Add(
                new PointBalance
                {
                    HostId = host,
                    Login = "overflow",
                    Amount = "2",
                    UpdatedAtUtc = DateTime.UtcNow,
                }
            );
            _ = await seed.SaveChangesAsync();
        }
        (await service.GetBoundedLeaderboardAsync(host, publicOnly: true, default)).ShouldBeNull();
    }

    [Test]
    public async Task PointsAmountBudget_DoesNotParseOrPublishOversizedHistoricalInput()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var context = new ViewerPortalTestContext(database);
        var host = await context.HostAsync("alpha", HostFeatureFlags.Points);
        await using (var seed = await database.CreateDbContextAsync())
        {
            _ = seed.PointBalances.Add(
                new PointBalance
                {
                    HostId = host,
                    Login = "viewer",
                    Amount = new string('0', 10_000) + "1",
                    UpdatedAtUtc = DateTime.UtcNow,
                }
            );
            _ = await seed.SaveChangesAsync();
        }
        var service = new PointBalanceService(database);
        (await service.GetBoundedLeaderboardAsync(host, publicOnly: true, default)).ShouldBeNull();
        (await service.GetBalanceAsync(host, "viewer", default)).Balance.Value.ShouldBe(1);
    }

    [Test]
    public async Task CancelledCaller_DoesNotReleaseUnfinishedOwnerSlotsOrStartCancelledQueuedWork()
    {
        using var scheduler = new PortalReadScheduler();
        using var caller = new CancellationTokenSource();
        var release = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var entered = 0;
        var active = Enumerable
            .Range(0, 4)
            .Select(index =>
                scheduler.ReadAsync(
                    async token =>
                    {
                        _ = Interlocked.Increment(ref entered);
                        return await release.Task;
                    },
                    caller.Token
                )
            )
            .ToArray();
        entered.ShouldBe(4);
        await caller.CancelAsync();
        _ = await Should.ThrowAsync<OperationCanceledException>(async () =>
            await active[0].WaitAsync(caller.Token)
        );
        using var waiting = new CancellationTokenSource();
        var queuedEntered = false;
        var queued = scheduler.ReadAsync(
            _ =>
            {
                queuedEntered = true;
                return Task.FromResult(true);
            },
            waiting.Token
        );
        queuedEntered.ShouldBeFalse();
        await waiting.CancelAsync();
        _ = await Should.ThrowAsync<OperationCanceledException>(async () => await queued);
        _ = release.TrySetResult(true);
        _ = await Task.WhenAll(active);
        queuedEntered.ShouldBeFalse();
        (await scheduler.ReadAsync(_ => Task.FromResult(true), default)).ShouldBeTrue();
    }
}
