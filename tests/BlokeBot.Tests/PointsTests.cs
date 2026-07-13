using System.Numerics;
using BlokeBot.Commands;
using BlokeBot.Eventing;
using BlokeBot.Features.Commands;
using BlokeBot.Features.HostedChannels.Authorization;
using BlokeBot.Features.Points;
using BlokeBot.Features.Points.Balances;
using BlokeBot.Features.Points.Commands;
using BlokeBot.Features.Points.Configuration;
using BlokeBot.Features.Points.Dashboard;
using BlokeBot.Features.Points.Gambling;
using BlokeBot.Features.Points.Replies;
using BlokeBot.Features.Replies;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Tests;

public sealed class PointsTests
{
    [Test]
    public void InvalidNegativeOrOversizedAmount_ParsingOrConstructing_RejectsValue()
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
    public void LargePointAmounts_FormattingForDisplay_UsesFourSignificantFiguresWithoutChangingValue()
    {
        var amount = PointAmount.ParseAbsolute("123456789012");

        amount.ToString().ShouldBe("123456789012");
        amount.ToDisplayString().ShouldBe("123.5B");
        PointAmount.ParseAbsolute("1234").ToDisplayString().ShouldBe("1,234");
        PointAmount.ParseAbsolute("10000").ToDisplayString().ShouldBe("10K");
        PointAmount.ParseAbsolute("999950").ToDisplayString().ShouldBe("1M");
        PointAmount.ParseAbsolute("1234567890123").ToDisplayString().ShouldBe("1.235T");
        PointAmount
            .ParseAbsolute("1234567890123456789012345678901234")
            .ToDisplayString()
            .ShouldBe("1.235 x 10^33");
    }

    [Test]
    public void AbsolutePercentageOrAllSpend_Parsing_ReturnsExpectedAmountAndRejectsInvalidInput()
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
    public void ChannelOrMentionPrefixedLogin_Normalizing_RemovesPrefixAndLowercases()
    {
        TwitchLogin.Normalize(" #Streamer ").ShouldBe("streamer");
        TwitchLogin.Normalize(" @Viewer ").ShouldBe("viewer");
    }

    [Test]
    public void ConfiguredChannelScopes_LoadingAuthorizationRequest_ReturnsNormalizedScopes()
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
        result.Success.ShouldBeFalse();
        result.FailureReason.ShouldBe(PointOperationFailureReason.UnknownUser);
        result.Message.ShouldBe("Twitch user @missingviewer was not found.");
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
        result.Success.ShouldBeTrue();
        balance.Login.ShouldBe("viewer");
        balance.Amount.ShouldBe("10");
    }

    [Test]
    public async Task LargePointMutation_AddingBalance_PersistsFullPrecisionInBalanceAndLedger()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var balances = new PointBalanceService(dbFactory);
        var amount = PointAmount.ParseAbsolute("123456789012");

        var result = await balances.AddAsync(
            hostId,
            "viewer",
            amount,
            "streamer",
            "test",
            CancellationToken.None
        );

        await using var db = await dbFactory.CreateDbContextAsync();
        var balance = await db.PointBalances.SingleAsync(CancellationToken.None);
        var ledger = await db.PointLedgerEntries.SingleAsync(CancellationToken.None);
        result.Success.ShouldBeTrue();
        result.Balance.ShouldBe(amount);
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
        result.Success.ShouldBeFalse();
        result.FailureReason.ShouldBe(PointOperationFailureReason.UnknownUser);
        result.Message.ShouldBe("No point balance found.");
        (await db.PointBalances.CountAsync(CancellationToken.None)).ShouldBe(0);
        (await db.PointLedgerEntries.CountAsync(CancellationToken.None)).ShouldBe(0);
    }

    [Test]
    public async Task UnknownCommandTarget_AddingPoints_ReturnsReplyWithoutBalance()
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
    public async Task MentionPrefixedCommandTarget_AddingPoints_StoresNormalizedLogin()
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

    [Test]
    public async Task WhisperConfiguredBalanceReply_ExecutingCommand_ReturnsWhisperResponse()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            db.PointsSettings.Add(new PointsSettings { HostId = hostId });
            db.ReplyDeliverySettings.Add(
                new ReplyDeliverySetting
                {
                    HostId = hostId,
                    Feature = ReplyDeliveryFeature.Points,
                    ScopeId = ReplyDeliverySettingWriter.HostScopeId,
                    ReplyKey = PointsReplyKeys.Balance,
                    Target = ReplyDeliveryTargets.Whisper,
                }
            );
            await db.SaveChangesAsync();
        }
        List<TwitchCommandResponse> responses = [];
        var strategy = new PointsBalanceCommandStrategy(
            new PointsCommandService(dbFactory),
            new PointBalanceService(dbFactory)
        );

        await strategy.ExecuteAsync(
            TypedCommandContext(
                hostId,
                "viewer",
                "streamer",
                "points",
                [],
                responses,
                PointsCommandKind.Points
            ),
            CancellationToken.None
        );

        var response = responses.Single();
        response.Target.ShouldBe(TwitchCommandResponseTarget.Whisper);
        response.Message.ShouldContain("viewer");
    }

    [Test]
    public async Task TwoUsersWithinGambleCooldown_ExecutingGambles_SuppressesOnlyRepeatedUser()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var clock = new ManualTimeProvider(
            new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero)
        );
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        await SeedPointsSettingsAsync(
            dbFactory,
            hostId,
            settings =>
            {
                settings.GamblingWinRatePercent = 100;
                settings.GamblingCooldownSeconds = 30;
            }
        );
        await AddBalanceAsync(dbFactory, hostId, "alice", "100");
        await AddBalanceAsync(dbFactory, hostId, "bob", "100");
        var strategy = CreateGambleStrategy(dbFactory, clock);
        List<string> replies = [];

        await strategy.ExecuteAsync(
            CommandContext(
                hostId,
                "alice",
                "streamer",
                "gamble",
                ["10"],
                replies,
                PointsCommandKind.Gamble
            ),
            CancellationToken.None
        );
        await strategy.ExecuteAsync(
            CommandContext(
                hostId,
                "alice",
                "streamer",
                "gamble",
                ["10"],
                replies,
                PointsCommandKind.Gamble
            ),
            CancellationToken.None
        );
        await strategy.ExecuteAsync(
            CommandContext(
                hostId,
                "bob",
                "streamer",
                "gamble",
                ["10"],
                replies,
                PointsCommandKind.Gamble
            ),
            CancellationToken.None
        );

        replies.ShouldBe([
            "alice gambled 10 points and won. Balance: 110.",
            "bob gambled 10 points and won. Balance: 110.",
        ]);
    }

    [Test]
    public async Task HostCooldownBelowMinimum_ExecutingGambles_UsesConfiguredMinimum()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var clock = new ManualTimeProvider(
            new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero)
        );
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        await SeedPointsSettingsAsync(
            dbFactory,
            hostId,
            settings =>
            {
                settings.GamblingWinRatePercent = 100;
                settings.GamblingCooldownSeconds = 1;
            }
        );
        await AddBalanceAsync(dbFactory, hostId, "alice", "100");
        var strategy = CreateGambleStrategy(dbFactory, clock, minimumGamblingCooldownSeconds: 5);
        List<string> replies = [];

        await strategy.ExecuteAsync(
            CommandContext(
                hostId,
                "alice",
                "streamer",
                "gamble",
                ["10"],
                replies,
                PointsCommandKind.Gamble
            ),
            CancellationToken.None
        );
        clock.Advance(TimeSpan.FromSeconds(1));
        await strategy.ExecuteAsync(
            CommandContext(
                hostId,
                "alice",
                "streamer",
                "gamble",
                ["10"],
                replies,
                PointsCommandKind.Gamble
            ),
            CancellationToken.None
        );
        clock.Advance(TimeSpan.FromSeconds(4));
        await strategy.ExecuteAsync(
            CommandContext(
                hostId,
                "alice",
                "streamer",
                "gamble",
                ["10"],
                replies,
                PointsCommandKind.Gamble
            ),
            CancellationToken.None
        );

        replies.ShouldBe([
            "alice gambled 10 points and won. Balance: 110.",
            "alice gambled 10 points and won. Balance: 120.",
        ]);
        var balance = await new PointBalanceService(dbFactory).GetBalanceAsync(
            hostId,
            "alice",
            CancellationToken.None
        );
        balance.Balance.ToString().ShouldBe("120");
    }

    [Test]
    public async Task ChangedGamblingCooldown_SavingConfiguration_RoundTripsValue()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var service = CreateConfigurationService(dbFactory);

        var config = await service.LoadConfigurationAsync(hostId, CancellationToken.None);
        config.GamblingCooldownSeconds = 42;
        await service.SaveConfigurationAsync(hostId, config, CancellationToken.None);
        var loaded = await service.LoadConfigurationAsync(hostId, CancellationToken.None);

        loaded.GamblingCooldownSeconds.ShouldBe(42);
        await using var db = await dbFactory.CreateDbContextAsync();
        var settings = await db.PointsSettings.SingleAsync(CancellationToken.None);
        settings.GamblingCooldownSeconds.ShouldBe(42);
    }

    private static CommandStrategyContext<PointsCommandKind, AppCommandRouteState> CommandContext(
        int hostId,
        string login,
        string channel,
        string commandName,
        IReadOnlyList<string> args,
        List<string> replies,
        PointsCommandKind kind = PointsCommandKind.AddPoints
    )
    {
        var command = TestCommandContext.Create(
            login,
            channel,
            commandName,
            args,
            (TwitchCommandResponse response, CancellationToken _) =>
            {
                replies.Add(response.Message);
                return ValueTask.CompletedTask;
            }
        );

        return new CommandStrategyContext<PointsCommandKind, AppCommandRouteState>(
            kind,
            new AppCommandRouteState(hostId),
            command,
            args
        );
    }

    private static GambleCommandStrategy CreateGambleStrategy(
        SqliteBlokeBotDbFactory dbFactory,
        TimeProvider clock,
        int minimumGamblingCooldownSeconds = 0
    )
    {
        return new(
            new PointsCommandService(dbFactory),
            new PointBalanceService(dbFactory),
            new FixedPointsRandom(),
            new PointsGamblingCooldownStore(clock),
            Options.Create(
                new BlokeBotOptions
                {
                    Points = new BlokeBotPointsOptions
                    {
                        MinimumGamblingCooldownSeconds = minimumGamblingCooldownSeconds,
                    },
                }
            )
        );
    }

    private static PointsConfigurationService CreateConfigurationService(
        SqliteBlokeBotDbFactory dbFactory
    )
    {
        var events = TestEventBus.Create<AppEventKind>();
        return new PointsConfigurationService(
            dbFactory,
            new CommandAliasRegistry(),
            new PointsChangeNotifier(events)
        );
    }

    private static async Task AddBalanceAsync(
        SqliteBlokeBotDbFactory dbFactory,
        int hostId,
        string login,
        string amount
    )
    {
        var result = await new PointBalanceService(dbFactory).AddAsync(
            hostId,
            login,
            PointAmount.ParseAbsolute(amount),
            "streamer",
            "test",
            CancellationToken.None
        );
        result.Success.ShouldBeTrue();
    }

    private static async Task SeedPointsSettingsAsync(
        SqliteBlokeBotDbFactory dbFactory,
        int hostId,
        Action<PointsSettings> configure
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var settings = new PointsSettings { HostId = hostId };
        configure(settings);
        db.PointsSettings.Add(settings);
        await db.SaveChangesAsync();
    }

    private static CommandStrategyContext<
        PointsCommandKind,
        AppCommandRouteState
    > TypedCommandContext(
        int hostId,
        string login,
        string channel,
        string commandName,
        IReadOnlyList<string> args,
        List<TwitchCommandResponse> responses,
        PointsCommandKind kind
    )
    {
        var command = TestCommandContext.Create(
            login,
            channel,
            commandName,
            args,
            (TwitchCommandResponse response, CancellationToken _) =>
            {
                responses.Add(response);
                return ValueTask.CompletedTask;
            }
        );

        return new CommandStrategyContext<PointsCommandKind, AppCommandRouteState>(
            kind,
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
        public HttpClient CreateClient(string name)
        {
            return new();
        }
    }

    private sealed class FixedPointTargetUserLookup(IEnumerable<string> existingUsers)
        : IPointTargetUserLookup
    {
        private readonly HashSet<string> _users = existingUsers
            .Select(TwitchLogin.Normalize)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        public Task<bool> ExistsAsync(string login, CancellationToken ct)
        {
            return Task.FromResult(_users.Contains(TwitchLogin.Normalize(login)));
        }
    }

    private sealed class FixedPointsRandom : IPointsRandom
    {
        public double NextDouble()
        {
            return 0;
        }

        public int Next(int minValue, int maxValue)
        {
            return minValue;
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _current = now;

        public override DateTimeOffset GetUtcNow()
        {
            return _current;
        }

        public void Advance(TimeSpan interval)
        {
            _current += interval;
        }
    }
}
