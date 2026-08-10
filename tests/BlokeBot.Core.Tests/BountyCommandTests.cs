using BlokeBot.Core.Features.Bounties;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class BountyCommandTests
{
    private static readonly DateTimeOffset _now = new(2026, 8, 10, 10, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task ChatCommands_ListViewPledgeAndPersistAuthenticatedModeratorActions()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedAsync(database);
        var clock = new FixedTimeProvider(_now);
        var service = new BountyService(database, TestEventBus.Create<AppEventKind>(), clock);
        var bounty = Success(
            await service.CreateAsync(
                hostId,
                new CreateBountyCommand(
                    Guid.NewGuid(),
                    "Chat challenge",
                    "Fund this from chat.",
                    new PointAmount(100),
                    _now.AddDays(1).UtcDateTime,
                    PointAmount.Zero,
                    BountyVisibility.Public,
                    BountyFailurePledgePolicy.Refund,
                    BountyRewardDistribution.Equal,
                    new BountyActor("streamer-id", "streamer")
                ),
                default
            )
        ).Value;
        var reference = bounty.PublicId.ToString("N")[..8];
        var services = new ServiceCollection();
        _ = services.AddSingleton<IDbContextFactory<BlokeBotDbContext>>(database);
        _ = services.AddSingleton(service);
        _ = services.AddSingleton<TimeProvider>(clock);
        _ = services.AddChatCommands().AddCommandModule<BountyCommandModule>();
        await using var provider = services.BuildServiceProvider();
        var dispatcher = provider.GetRequiredService<ChatCommandDispatcher>();
        var responses = new List<string>();

        await DispatchAsync(dispatcher, Message("viewer", $"!bounty {reference}"), responses);
        await DispatchAsync(dispatcher, Message("viewer", $"!bountyopen {reference}"), responses);
        responses[0].ShouldContain("Chat challenge");
        responses[1].ShouldContain("moderator-only");

        await DispatchAsync(
            dispatcher,
            Message(
                "moderator",
                $"!bountyopen {reference} | Ready to fund",
                new Dictionary<string, string> { ["mod"] = "1", ["user-id"] = "moderator-id" }
            ),
            responses
        );
        await DispatchAsync(
            dispatcher,
            Message(
                "viewer",
                $"!bountypledge {reference} 25",
                new Dictionary<string, string> { ["user-id"] = "viewer-id" }
            ),
            responses
        );
        await DispatchAsync(
            dispatcher,
            Message(
                "moderator",
                $"!bountyaccept {reference} | Accepted below target",
                new Dictionary<string, string> { ["mod"] = "1", ["user-id"] = "moderator-id" }
            ),
            responses
        );

        responses.ShouldContain(static value => value.Contains("Pledged 25 points"));
        responses[^1].ShouldContain("Accepted");
        await using var verify = await database.CreateDbContextAsync();
        (await verify.Bounties.SingleAsync()).Status.ShouldBe(BountyStatus.Accepted);
        (await verify.PointBalances.SingleAsync()).Amount.ShouldBe("75");
        var acceptedAudit = await verify.BountyModerationAudits.SingleAsync(value =>
            value.Action == BountyAuditAction.Accepted
        );
        acceptedAudit.ActorTwitchUserId.ShouldBe("moderator-id");
        acceptedAudit.ActorLogin.ShouldBe("moderator");
        acceptedAudit.Reason.ShouldBe("Accepted below target");
    }

    [Test]
    public async Task DisabledDependency_SafelySuppressesEveryBountyCommandBeforeMutation()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedAsync(database);
        var clock = new FixedTimeProvider(_now);
        var service = new BountyService(database, TestEventBus.Create<AppEventKind>(), clock);
        await using (var disable = await database.CreateDbContextAsync())
        {
            var host = await disable.Hosts.SingleAsync();
            host.EnabledFeatures &= ~HostFeatureFlags.Points;
            _ = await disable.SaveChangesAsync();
        }
        var services = new ServiceCollection();
        _ = services.AddSingleton<IDbContextFactory<BlokeBotDbContext>>(database);
        _ = services.AddSingleton(service);
        _ = services.AddSingleton<TimeProvider>(clock);
        _ = services.AddChatCommands().AddCommandModule<BountyCommandModule>();
        await using var provider = services.BuildServiceProvider();
        var dispatcher = provider.GetRequiredService<ChatCommandDispatcher>();
        var responses = new List<string>();

        await DispatchAsync(dispatcher, Message("viewer", "!bounties"), responses);
        await DispatchAsync(
            dispatcher,
            Message(
                "moderator",
                "!bountycreate 100 24 public refund equal 0 Suppressed",
                new Dictionary<string, string> { ["mod"] = "1", ["user-id"] = "moderator-id" }
            ),
            responses
        );

        responses.ShouldBeEmpty();
        await using var verify = await database.CreateDbContextAsync();
        (await verify.Bounties.CountAsync()).ShouldBe(0);
        (await verify.PointLedgerEntries.CountAsync()).ShouldBe(0);
        (await verify.BountyEvents.CountAsync()).ShouldBe(0);
    }

    private static BountyResult<T>.Succeeded Success<T>(BountyResult<T> result) =>
        result.Match(
            static value => value,
            static rejected => throw new InvalidOperationException(rejected.Reason.Message)
        );

    private static async Task DispatchAsync(
        ChatCommandDispatcher dispatcher,
        ChatMessage message,
        List<string> responses
    ) =>
        await dispatcher.DispatchResponsesAsync(
            message,
            (response, _) =>
            {
                responses.Add(response.Message);
                return ValueTask.CompletedTask;
            },
            default
        );

    private static ChatMessage Message(
        string login,
        string text,
        IReadOnlyDictionary<string, string>? tags = null
    )
    {
        var values = new Dictionary<string, string>(tags ?? new Dictionary<string, string>())
        {
            ["id"] = Guid.NewGuid().ToString(),
        };
        return new ChatMessage(login, "streamer", text, "raw", values);
    }

    private static async Task<int> SeedAsync(SqliteBlokeBotDbFactory database)
    {
        await using var db = await database.CreateDbContextAsync();
        var host = new BotHost
        {
            TwitchUserId = "streamer-id",
            Login = "streamer",
            DisplayName = "Streamer",
            EnabledFeatures = HostFeatureFlags.All,
            CreatedAtUtc = _now.UtcDateTime,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        _ = db.PointBalances.Add(
            new PointBalance
            {
                HostId = host.Id,
                Login = "viewer",
                Amount = "100",
                UpdatedAtUtc = _now.UtcDateTime,
            }
        );
        _ = await db.SaveChangesAsync();
        return host.Id;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
