using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Core.Tests;

public sealed class CustomCommandInvocationClaimStoreTests
{
    [Test]
    public async Task StreamClaimCleanup_Claiming_RemovesBoundedExpiredBatchAndPreservesBoundaries()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var now = new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.Zero);
        var seed = await SeedCommandAsync(dbFactory);
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            db.CustomCommandInvocationClaims.AddRange(
                Enumerable
                    .Range(0, CustomCommandInvocationClaimStore.CleanupBatchSize + 1)
                    .Select(index =>
                        Claim(
                            seed,
                            twitchUserId: null,
                            twitchStreamId: $"expired-{index}",
                            now.UtcDateTime.AddDays(-7).AddTicks(-1)
                        )
                    )
            );
            db.CustomCommandInvocationClaims.AddRange(
                Claim(
                    seed,
                    twitchUserId: null,
                    twitchStreamId: "current",
                    now.UtcDateTime.AddDays(-30)
                ),
                Claim(
                    seed,
                    twitchUserId: null,
                    twitchStreamId: "boundary",
                    now.UtcDateTime.AddDays(-7)
                ),
                Claim(
                    seed,
                    twitchUserId: "lifetime-user",
                    twitchStreamId: null,
                    now.UtcDateTime.AddDays(-30)
                )
            );
            await db.SaveChangesAsync();
        }

        var store = new CustomCommandInvocationClaimStore(new FixedTimeProvider(now));
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var outcome = await store.TryClaimAsync(
                db,
                new(
                    seed.HostId,
                    seed.CommandId,
                    new CustomCommandInvocationScope.OncePerStream("current")
                ),
                CancellationToken.None
            );
            outcome.ShouldBeOfType<CustomCommandInvocationClaimOutcome.AlreadyUsed>();
        }

        await using var verify = await dbFactory.CreateDbContextAsync();
        (
            await verify.CustomCommandInvocationClaims.CountAsync(claim =>
                claim.TwitchStreamId!.StartsWith("expired-")
            )
        ).ShouldBe(1);
        (
            await verify.CustomCommandInvocationClaims.AnyAsync(claim =>
                claim.TwitchStreamId == "current"
            )
        ).ShouldBeTrue();
        (
            await verify.CustomCommandInvocationClaims.AnyAsync(claim =>
                claim.TwitchStreamId == "boundary"
            )
        ).ShouldBeTrue();
        (
            await verify.CustomCommandInvocationClaims.AnyAsync(claim =>
                claim.TwitchUserId == "lifetime-user" && claim.TwitchStreamId == null
            )
        ).ShouldBeTrue();
    }

    private static CustomCommandInvocationClaim Claim(
        Seed seed,
        string? twitchUserId,
        string? twitchStreamId,
        DateTime claimedAtUtc
    )
    {
        return new()
        {
            HostId = seed.HostId,
            CustomCommandId = seed.CommandId,
            TwitchUserId = twitchUserId,
            TwitchStreamId = twitchStreamId,
            ClaimedAtUtc = claimedAtUtc,
        };
    }

    private static async Task<Seed> SeedCommandAsync(SqliteBlokeBotDbFactory dbFactory)
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
        var command = new CustomCommand
        {
            HostId = host.Id,
            Name = "Limited command",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };
        db.CustomCommands.Add(command);
        await db.SaveChangesAsync();
        return new Seed(host.Id, command.Id);
    }

    private sealed record Seed(int HostId, int CommandId);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return now;
        }
    }
}
