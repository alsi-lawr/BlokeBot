using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Eventing;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Core.Tests;

public sealed class CustomCommandInvocationResetTests
{
    [Test]
    public async Task OneViewerAndAllViewers_ResettingLifetimeClaims_DeletesTargetsAndAuditsSnapshots()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var seed = await SeedClaimsAsync(dbFactory);
        var clock = new FixedTimeProvider(
            new DateTimeOffset(2026, 7, 22, 12, 30, 0, TimeSpan.Zero)
        );
        var resets = new CustomCommandInvocationResetService(
            dbFactory,
            new StaticViewerResolver(new CustomCommandViewer("alice-id", "alice")),
            TestEventBus.Create<AppEventKind>(),
            clock
        );
        var actor = new CustomCommandResetActor("manager-id", "Manager");

        var one = await resets.ResetViewerAsync(
            seed.HostId,
            seed.CommandId,
            actor,
            "Alice",
            CancellationToken.None
        );
        var all = await resets.ResetAllViewersAsync(
            seed.HostId,
            seed.CommandId,
            actor,
            CancellationToken.None
        );

        one.ShouldBe(new CustomCommandInvocationResetOutcome.Reset(1));
        all.ShouldBe(new CustomCommandInvocationResetOutcome.Reset(1));
        await using var db = await dbFactory.CreateDbContextAsync();
        var remaining = await db.CustomCommandInvocationClaims.ToArrayAsync();
        remaining.ShouldHaveSingleItem().TwitchStreamId.ShouldBe("stream-id");
        var audits = await db
            .CustomCommandInvocationResetAudits.OrderBy(audit => audit.Id)
            .ToArrayAsync();
        audits.Length.ShouldBe(2);
        audits[0].Scope.ShouldBe(CustomCommandInvocationResetScope.OneViewer);
        audits[0].TargetTwitchUserId.ShouldBe("alice-id");
        audits[0].TargetLogin.ShouldBe("alice");
        audits[0].AffectedClaimCount.ShouldBe(1);
        audits[1].Scope.ShouldBe(CustomCommandInvocationResetScope.AllViewers);
        audits[1].TargetTwitchUserId.ShouldBeNull();
        audits.All(audit => audit.CommandName == "Limited command").ShouldBeTrue();
        audits.All(audit => audit.ActorTwitchUserId == "manager-id").ShouldBeTrue();
        audits.All(audit => audit.ActorLogin == "manager").ShouldBeTrue();
        audits.All(audit => audit.ResetAtUtc == clock.GetUtcNow().UtcDateTime).ShouldBeTrue();

        db.ChangeTracker.Clear();
        var command = await db.CustomCommands.SingleAsync(stored => stored.Id == seed.CommandId);
        db.CustomCommands.Remove(command);
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();
        (await db.CustomCommandInvocationClaims.CountAsync()).ShouldBe(0);
        var preservedAudits = await db.CustomCommandInvocationResetAudits.ToArrayAsync();
        preservedAudits.Length.ShouldBe(2);
        preservedAudits.All(audit => audit.CustomCommandId is null).ShouldBeTrue();
        preservedAudits.All(audit => audit.CommandName == "Limited command").ShouldBeTrue();
    }

    private static async Task<Seed> SeedClaimsAsync(SqliteBlokeBotDbFactory dbFactory)
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
        db.CustomCommandInvocationClaims.AddRange(
            new CustomCommandInvocationClaim
            {
                HostId = host.Id,
                CustomCommandId = command.Id,
                TwitchUserId = "alice-id",
                ClaimedAtUtc = DateTime.UtcNow,
            },
            new CustomCommandInvocationClaim
            {
                HostId = host.Id,
                CustomCommandId = command.Id,
                TwitchUserId = "bob-id",
                ClaimedAtUtc = DateTime.UtcNow,
            },
            new CustomCommandInvocationClaim
            {
                HostId = host.Id,
                CustomCommandId = command.Id,
                TwitchStreamId = "stream-id",
                ClaimedAtUtc = DateTime.UtcNow,
            }
        );
        await db.SaveChangesAsync();
        return new Seed(host.Id, command.Id);
    }

    private sealed record Seed(int HostId, int CommandId);

    private sealed class StaticViewerResolver(CustomCommandViewer viewer)
        : ICustomCommandViewerResolver
    {
        public Task<CustomCommandViewerResolution> ResolveAsync(string login, CancellationToken ct)
        {
            return Task.FromResult<CustomCommandViewerResolution>(
                new CustomCommandViewerResolution.Found(viewer)
            );
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return now;
        }
    }
}
