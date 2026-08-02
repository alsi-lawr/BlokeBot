using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Core.Features.Points.Commands;
using BlokeBot.Core.Features.Points.Replies;
using BlokeBot.Core.Features.Replies;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class PointsCommandTests : PointsTestBase
{
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
                    Feature = ReplyFeature.Points,
                    ScopeId = ReplyDeliverySettingWriter.HostScopeId,
                    ReplyKey = PointsReplyKeys.Balance,
                    Target = ReplyDeliveryTarget.Whisper,
                }
            );
            await db.SaveChangesAsync();
        }
        List<CommandResponse> responses = [];
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
        response.Target.ShouldBe(CommandResponseTarget.Whisper);
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
    public async Task NegativeGamblingCooldown_ExecutingGamble_ReturnsUnavailableWithoutMutation()
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
                settings.GamblingCooldownSeconds = -1;
            }
        );
        await AddBalanceAsync(dbFactory, hostId, "alice", "100");
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

        replies.ShouldBe(["Gambling is unavailable. The wait between gambles cannot be negative."]);
        var balance = await new PointBalanceService(dbFactory).GetBalanceAsync(
            hostId,
            "alice",
            CancellationToken.None
        );
        balance.Balance.ToString().ShouldBe("100");
    }
}
