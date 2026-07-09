using System.Numerics;
using System.Reflection;
using BlokeBot.Commands;
using BlokeBot.Eventing;
using BlokeBot.Features.Commands;
using BlokeBot.Features.HostedChannels.Authorization;
using BlokeBot.Features.Points;
using BlokeBot.Features.Points.Balances;
using BlokeBot.Features.Points.Commands;
using BlokeBot.Features.Points.Dashboard;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Tests;

public sealed class PointsTests
{
    [Test]
    public void Point_amount_rejects_invalid_negative_and_over_cap_values()
    {
        PointAmount.TryParseAbsolute("100", out var amount).ShouldBeTrue();
        amount.Value.ShouldBe(new BigInteger(100));

        PointAmount.TryParseAbsolute("10.5", out _).ShouldBeFalse();
        Should.Throw<ArgumentOutOfRangeException>(() => new PointAmount(-1));
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new PointAmount(PointAmount.MaximumValue + 1)
        );
    }

    [Test]
    public void Point_amount_rounds_large_persisted_balances_to_four_significant_figures()
    {
        var rounded = PointAmount.ParseAbsolute("123456789012").RoundForPersistence();

        rounded.ToString().ShouldBe("123500000000");
        rounded.ToDisplayString().ShouldBe("123.5e9");
    }

    [Test]
    public void Spend_amount_parser_supports_absolute_percentage_and_all()
    {
        var balance = PointAmount.ParseAbsolute("2500");

        PointAmountArgumentParser.ParseSpendAmount("100", balance).ToString().ShouldBe("100");
        PointAmountArgumentParser.ParseSpendAmount("10%", balance).ToString().ShouldBe("250");
        PointAmountArgumentParser.ParseSpendAmount("all", balance).ToString().ShouldBe("2500");
        Should.Throw<FormatException>(() =>
            PointAmountArgumentParser.ParseSpendAmount("1%", PointAmount.ParseAbsolute("1"))
        );
        Should.Throw<FormatException>(() =>
            PointAmountArgumentParser.ParseSpendAmount("101%", balance)
        );
        Should.Throw<FormatException>(() => PointAmountArgumentParser.ParseAbsoluteOnly("50%"));
    }

    [Test]
    public void Twitch_login_normalization_strips_channel_and_mention_prefixes()
    {
        TwitchLogin.Normalize(" #Streamer ").ShouldBe("streamer");
        TwitchLogin.Normalize(" @Viewer ").ShouldBe("viewer");
    }

    [Test]
    public void Channel_authorization_uses_configured_scopes()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["TwitchBot:Identity:ClientId"] = "client",
                    ["TwitchBot:ChannelAuthorization:Scopes:0"] = "channel:bot",
                    ["TwitchBot:ChannelAuthorization:Scopes:1"] = "bits:read",
                }
            )
            .Build();
        var httpClientFactory = new FakeHttpClientFactory();
        var service = new ChannelBotOAuthService(
            configuration,
            new TwitchOAuthApiClient(httpClientFactory)
        );
        var scopes = service.RequestedScopes();

        scopes.ShouldBe(["bits:read", "channel:bot"]);
    }

    [Test]
    public async Task Dashboard_add_rejects_unknown_twitch_user_without_creating_balance()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var service = new PointsDashboardService(
            new PointBalanceService(dbFactory),
            null!,
            new PointsChangeNotifier(new EventBus<AppEventKind>()),
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
        result.Success.ShouldBeFalse();
        result.FailureReason.ShouldBe(PointOperationFailureReason.UnknownUser);
        result.Message.ShouldBe("Twitch user @missingviewer was not found.");
        (await db.PointBalances.CountAsync(CancellationToken.None)).ShouldBe(0);
    }

    [Test]
    public async Task Dashboard_add_strips_mention_prefix_before_storing_balance()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var service = new PointsDashboardService(
            new PointBalanceService(dbFactory),
            null!,
            new PointsChangeNotifier(new EventBus<AppEventKind>()),
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
        result.Success.ShouldBeTrue();
        balance.Login.ShouldBe("viewer");
        balance.Amount.ShouldBe("10");
    }

    [Test]
    public async Task Dashboard_remove_balance_deletes_row_and_writes_audit_ledger()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var balances = new PointBalanceService(dbFactory);
        var service = new PointsDashboardService(
            balances,
            null!,
            new PointsChangeNotifier(new EventBus<AppEventKind>()),
            new FixedPointTargetUserLookup([])
        );
        await balances.AddAsync(
            hostId,
            "viewer",
            PointAmount.ParseAbsolute("25"),
            "streamer",
            "test",
            CancellationToken.None
        );

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
        result.Success.ShouldBeTrue();
        result.Message.ShouldBe("Point balance removed.");
        (await db.PointBalances.CountAsync(CancellationToken.None)).ShouldBe(0);
        ledger.Count.ShouldBe(2);
        ledger[^1].Kind.ShouldBe("DeleteBalance");
        ledger[^1].Login.ShouldBe("viewer");
        ledger[^1].Delta.ShouldBe("-25");
        ledger[^1].BalanceAfter.ShouldBe("0");
        ledger[^1].ActorLogin.ShouldBe("streamer");
        ledger[^1].Note.ShouldBe("dashboard");
    }

    [Test]
    public async Task Dashboard_remove_missing_balance_does_not_create_row()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var service = new PointsDashboardService(
            new PointBalanceService(dbFactory),
            null!,
            new PointsChangeNotifier(new EventBus<AppEventKind>()),
            new FixedPointTargetUserLookup([])
        );

        var result = await service.RemoveBalanceAsync(
            hostId,
            "missingviewer",
            "streamer",
            CancellationToken.None
        );

        await using var db = await dbFactory.CreateDbContextAsync();
        result.Success.ShouldBeFalse();
        result.FailureReason.ShouldBe(PointOperationFailureReason.UnknownUser);
        result.Message.ShouldBe("No point balance found.");
        (await db.PointBalances.CountAsync(CancellationToken.None)).ShouldBe(0);
        (await db.PointLedgerEntries.CountAsync(CancellationToken.None)).ShouldBe(0);
    }

    [Test]
    public async Task Addpoints_command_rejects_unknown_twitch_user_without_creating_balance()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        List<string> replies = [];
        var strategy = new AddPointsCommandStrategy(
            new PointsCommandService(dbFactory),
            new PointBalanceService(dbFactory),
            new FixedPointTargetUserLookup([])
        );

        await strategy.ExecuteAsync(
            CommandContext(
                hostId,
                "moderator",
                "streamer",
                "addpoints",
                ["@missingviewer", "10"],
                replies
            ),
            CancellationToken.None
        );

        await using var db = await dbFactory.CreateDbContextAsync();
        replies.ShouldBe(["Twitch user @missingviewer was not found."]);
        (await db.PointBalances.CountAsync(CancellationToken.None)).ShouldBe(0);
    }

    [Test]
    public async Task Addpoints_command_strips_mention_prefix_before_storing_balance()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        List<string> replies = [];
        var strategy = new AddPointsCommandStrategy(
            new PointsCommandService(dbFactory),
            new PointBalanceService(dbFactory),
            new FixedPointTargetUserLookup(["viewer"])
        );

        await strategy.ExecuteAsync(
            CommandContext(
                hostId,
                "moderator",
                "streamer",
                "addpoints",
                ["@Viewer", "10"],
                replies
            ),
            CancellationToken.None
        );

        await using var db = await dbFactory.CreateDbContextAsync();
        var balance = await db.PointBalances.SingleAsync(CancellationToken.None);
        replies.ShouldBe(["Added 10 points to viewer."]);
        balance.Login.ShouldBe("viewer");
        balance.Amount.ShouldBe("10");
    }

    private static CommandStrategyContext<PointsCommandKind, AppCommandRouteState> CommandContext(
        int hostId,
        string login,
        string channel,
        string commandName,
        IReadOnlyList<string> args,
        List<string> replies
    )
    {
        var constructor = typeof(TwitchCommandContext)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(constructor => constructor.GetParameters().Length == 4);
        var text = $"!{commandName} {string.Join(' ', args)}";
        var command = (TwitchCommandContext)
            constructor.Invoke([
                new TwitchChatMessage(
                    login,
                    channel,
                    text,
                    $":{login}!u@h PRIVMSG #{channel} :{text}",
                    new Dictionary<string, string>()
                ),
                commandName,
                new EmptyServiceProvider(),
                new Func<string, CancellationToken, ValueTask>(
                    (message, _) =>
                    {
                        replies.Add(message);
                        return ValueTask.CompletedTask;
                    }
                ),
            ]);

        return new CommandStrategyContext<PointsCommandKind, AppCommandRouteState>(
            PointsCommandKind.AddPoints,
            new AppCommandRouteState(hostId),
            command,
            args
        );
    }

    private static async Task<int> SeedHostAsync(SqliteBlokeBotDbFactory dbFactory, string login)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var host = new BotHost
        {
            Login = login,
            DisplayName = login,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.Hosts.Add(host);
        await db.SaveChangesAsync();
        return host.Id;
    }

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private sealed class FixedPointTargetUserLookup(IEnumerable<string> existingUsers)
        : IPointTargetUserLookup
    {
        private readonly HashSet<string> users = existingUsers
            .Select(TwitchLogin.Normalize)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        public Task<bool> ExistsAsync(string login, CancellationToken ct) =>
            Task.FromResult(users.Contains(TwitchLogin.Normalize(login)));
    }
}
