using BlokeBot.Core.Features.Points;
using BlokeBot.Core.Features.Points.Configuration;
using BlokeBot.Core.Features.Points.Replies;
using BlokeBot.Core.Features.Toasts;
using BlokeBot.Persistence.Models;
using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlokeBot.Core.Tests;

/// <summary>
/// The staged redesign replaced twenty-three reply text areas, a sixteen-checkbox whisper section,
/// nine comma-grammar alias fields and two free-text prize boxes with grouped narrated rows, chips
/// and paired steppers. These cover the preservation claims that reading the markup does not
/// settle: that every message survives in its group with its delivery choice, that the whisper rule
/// still gates and recovers now that the shared section carrying it is gone, that the alias chips
/// still write the comma model the validator consumes, and that the prize pair holds the
/// multiples-of-ten and min-not-above-max rules while still letting a typed value reach the
/// validator.
/// </summary>
public sealed class PointsSettingsStudioUiTests
{
    private static readonly IReadOnlyList<string> _chatOnlyReplies =
    [
        "gamble-win",
        "gamble-loss",
        "giveaway-started",
        "giveaway-status",
        "giveaway-ended",
        "giveaway-no-entrants",
        "giveaway-cancelled",
    ];

    [Test]
    public async Task Replies_KeepEveryMessageInItsGroupWithItsDeliveryChoiceOrChatOnlyNote()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedAsync(dbFactory);
        await using var context = CreateContext(dbFactory, hostId);
        var page = context.Render<PointsConfigurationPage>();
        var defaults = PointsDefaults.Replies(PointsDefaults.Settings());

        page.FindAll(".studio-replies__group")
            .Select(static group => group.TextContent)
            .ShouldBe(["Balances & moving points", "Gambling", "Giveaways"]);
        page.FindAll("[data-reply]")
            .Select(static row => row.GetAttribute("data-reply"))
            .ShouldBe([
                "balance",
                "other-balance",
                "transfer",
                "add",
                "remove",
                "invalid-amount",
                "insufficient-balance",
                "moderator-only",
                "gamble-win",
                "gamble-loss",
                "giveaway-started",
                "giveaway-status",
                "giveaway-joined",
                "giveaway-already-joined",
                "giveaway-ended",
                "giveaway-no-entrants",
                "giveaway-cancelled",
                "giveaway-already-active",
                "giveaway-not-active",
                "giveaway-cooldown",
                "stream-offline",
                "not-eligible",
                "follower-unavailable",
            ]);
        page.FindAll("[data-reply] .studio-reply__excerpt")
            .Select(static excerpt => excerpt.TextContent)
            .ShouldBe([
                defaults.BalanceReply,
                defaults.OtherBalanceReply,
                defaults.TransferReply,
                defaults.AddReply,
                defaults.RemoveReply,
                defaults.InvalidAmountReply,
                defaults.InsufficientBalanceReply,
                defaults.ModeratorOnlyReply,
                defaults.GamblingWinReply,
                defaults.GamblingLoseReply,
                defaults.GiveawayStartedReply,
                defaults.GiveawayUpdateReply,
                defaults.GiveawayJoinedReply,
                defaults.GiveawayAlreadyJoinedReply,
                defaults.GiveawayEndedReply,
                defaults.GiveawayNoEntrantsReply,
                defaults.GiveawayCancelledReply,
                defaults.GiveawayAlreadyActiveReply,
                defaults.GiveawayNotActiveReply,
                defaults.GiveawayCooldownReply,
                defaults.StreamOfflineReply,
                defaults.NotEligibleReply,
                defaults.FollowerEligibilityUnavailableReply,
            ]);
        page.FindAll("[data-reply] .studio-reply__state")
            .ShouldAllBe(static state => state.TextContent == "default");

        OpenEveryReply(page);

        foreach (var key in _chatOnlyReplies)
        {
            page.FindAll($"[data-reply='{key}'] .studio-segmented").ShouldBeEmpty();
            page.Find($"[data-reply='{key}'] .studio-reply__content p")
                .TextContent.ShouldStartWith("Always sent to chat");
        }

        page.FindAll("[data-reply] .studio-segmented")
            .Count.ShouldBe(PointsReplyKeys.WhisperableKeys.Count);
        page.FindAll("[data-reply] textarea").Count.ShouldBe(23);
    }

    [Test]
    public async Task WhisperWithoutACustomBot_DisablesEveryDeliveryChoiceAndOffersRecovery()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedAsync(dbFactory);
        await using var context = CreateContext(dbFactory, hostId);
        var page = context.Render<PointsConfigurationPage>();

        OpenEveryReply(page);

        page.FindAll("[data-reply] .studio-segmented__option")
            .ShouldAllBe(static option => option.HasAttribute("disabled"));
        page.FindAll("[data-reply] .studio-segmented__option")
            .ShouldAllBe(static option =>
                option.GetAttribute("aria-describedby") == "reply-delivery-whisper-recovery"
            );
        page.Find("#reply-delivery-whisper-recovery")
            .TextContent.ShouldContain("need a connected custom bot");
        _ = page.Find("a[href='/host#custom-bot']").ShouldNotBeNull();
    }

    [Test]
    public async Task WhisperWithACustomBot_ChoosingWhisperOnAReply_PersistsThatDelivery()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedAsync(dbFactory, whisperCapable: true);
        await using var context = CreateContext(dbFactory, hostId);
        var page = context.Render<PointsConfigurationPage>();
        page.Find("[data-reply='insufficient-balance'] .studio-reply__header").Click();

        page.FindAll("[data-reply='insufficient-balance'] .studio-segmented__option")
            .Single(static option => option.TextContent == "Whisper")
            .Click();

        page.Find("[data-reply='insufficient-balance'] .studio-reply__state")
            .TextContent.ShouldBe("whisper");
        Save(page);
        page.WaitForAssertion(() =>
            page.Find("[data-reply='insufficient-balance'] .studio-reply__state")
                .TextContent.ShouldBe("whisper")
        );
        await using var db = await dbFactory.CreateDbContextAsync();
        var whispered = await db
            .ReplyDeliverySettings.Where(setting =>
                setting.HostId == hostId && setting.Target == ReplyDeliveryTarget.Whisper
            )
            .Select(setting => setting.ReplyKey)
            .ToListAsync();
        whispered.ShouldBe([PointsReplyKeys.InsufficientBalance]);
    }

    [Test]
    public async Task CommandWords_AddedAndRemovedAsChips_PersistTheChannelCommandWords()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedAsync(dbFactory);
        await using var context = CreateContext(dbFactory, hostId);
        var page = context.Render<PointsConfigurationPage>();

        page.Find("#points-gamble-aliases").Input("wager");
        page.Find("#points-gamble-aliases").KeyDown("Enter");
        page.FindAll("[data-action='remove-command-word']")
            .Single(static button => button.GetAttribute("aria-label") == "Remove !bet from Gamble")
            .Click();
        Save(page);

        await using var db = await dbFactory.CreateDbContextAsync();
        page.WaitForAssertion(() =>
            page.FindAll(
                    "[data-action='remove-command-word'][aria-label='Remove !wager from Gamble']"
                )
                .Count.ShouldBe(1)
        );
        var gambleWords = await db
            .CommandAliases.Where(alias =>
                alias.HostId == hostId && alias.Kind == AppCommandKind.Gamble
            )
            .Select(alias => alias.Alias)
            .OrderBy(alias => alias)
            .ToListAsync();
        gambleWords.ShouldBe(["gamble", "wager"]);
    }

    [Test]
    public async Task PrizeRange_SteppingEitherSide_ClampsAtTheSiblingAndStillPersistsTypedAmounts()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedAsync(dbFactory);
        await using var context = CreateContext(dbFactory, hostId);
        var page = context.Render<PointsConfigurationPage>();

        Step(page, "points-giveaway-minimum-payout", "increment");
        Value(page, "points-giveaway-minimum-payout").ShouldBe("20");

        for (var press = 0; press < 12; press++)
        {
            Step(page, "points-giveaway-minimum-payout", "increment");
        }

        Value(page, "points-giveaway-minimum-payout").ShouldBe("100");
        Value(page, "points-giveaway-maximum-payout").ShouldBe("100");

        Step(page, "points-giveaway-maximum-payout", "decrement");
        Value(page, "points-giveaway-maximum-payout").ShouldBe("100");

        Step(page, "points-giveaway-minimum-payout", "decrement");
        Step(page, "points-giveaway-maximum-payout", "decrement");
        Value(page, "points-giveaway-maximum-payout").ShouldBe("90");

        page.Find("#points-giveaway-minimum-payout").Input("40");
        page.Find("#points-giveaway-maximum-payout").Input("1000000000000000000000000000000");
        Save(page);

        await using var db = await dbFactory.CreateDbContextAsync();
        page.WaitForAssertion(() =>
            Value(page, "points-giveaway-maximum-payout")
                .ShouldBe("1000000000000000000000000000000")
        );
        var settings = await db.PointsSettings.SingleAsync(x => x.HostId == hostId);
        settings.GiveawayMinimumPayout.ShouldBe("40");
        settings.GiveawayMaximumPayout.ShouldBe("1000000000000000000000000000000");
    }

    [Test]
    public async Task TypedPrizeBreakingTheStepRule_Saving_ReportsItInsteadOfRewritingIt()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedAsync(dbFactory);
        await using var context = CreateContext(dbFactory, hostId);
        var toasts = context.Services.GetRequiredService<ToastService>();
        var page = context.Render<PointsConfigurationPage>();

        page.Find("#points-giveaway-minimum-payout").Input("55");
        Save(page);

        page.WaitForAssertion(() => Value(page, "points-giveaway-minimum-payout").ShouldBe("55"));
        var error = toasts.Current.ShouldHaveSingleItem();
        error.Kind.ShouldBe(ToastKind.Error);
        error.Message.ShouldContain("Giveaway prizes must be multiples of 10.");
        await using var db = await dbFactory.CreateDbContextAsync();
        (await db.PointsSettings.AnyAsync(x => x.HostId == hostId)).ShouldBeFalse();
    }

    private static void Save(IRenderedComponent<PointsConfigurationPage> page) =>
        page.FindAll("button")
            .Single(static button => button.TextContent.Trim() == "Save changes")
            .Click();

    private static void Step(
        IRenderedComponent<PointsConfigurationPage> page,
        string id,
        string action
    ) => page.Find($"#{id}").ParentElement!.QuerySelector($"[data-action='{action}']")!.Click();

    private static string? Value(IRenderedComponent<PointsConfigurationPage> page, string id) =>
        page.Find($"#{id}").GetAttribute("value");

    private static void OpenEveryReply(IRenderedComponent<PointsConfigurationPage> page)
    {
        foreach (
            var key in page.FindAll("[data-reply]")
                .Select(row => row.GetAttribute("data-reply"))
                .ToArray()
        )
        {
            page.Find($"[data-reply='{key}'] .studio-reply__header").Click();
        }
    }

    private static BunitContext CreateContext(SqliteBlokeBotDbFactory dbFactory, int hostId)
    {
        var context = UiTestContextFactory.Create(dbFactory, hostId);
        _ = context.Services.AddSingleton<PointsChangeNotifier>();
        _ = context.Services.AddSingleton<PointsConfigurationService>();
        _ = context.ComponentFactories.AddStub<PointsEligibilitySelector>();
        return context;
    }

    private static async Task<int> SeedAsync(
        SqliteBlokeBotDbFactory dbFactory,
        bool whisperCapable = false
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var host = new BotHost
        {
            Login = "streamer",
            DisplayName = "Streamer",
            EnabledFeatures = HostFeatureFlags.Points,
            CreatedAtUtc = DateTime.UtcNow,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        db.CommandAliases.AddRange(
            new CommandAlias
            {
                HostId = host.Id,
                Kind = AppCommandKind.Gamble,
                Alias = "gamble",
            },
            new CommandAlias
            {
                HostId = host.Id,
                Kind = AppCommandKind.Gamble,
                Alias = "bet",
            }
        );
        if (whisperCapable)
        {
            _ = db.HostBotAccountSettings.Add(
                new HostBotAccountSettings
                {
                    HostId = host.Id,
                    OverrideEnabled = true,
                    WhisperResponsesEnabled = true,
                }
            );
        }

        _ = await db.SaveChangesAsync();
        return host.Id;
    }
}
