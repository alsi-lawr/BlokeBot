using BlokeBot.Core.Features.Guessing.Configuration;
using BlokeBot.Core.Features.Guessing.Game;
using BlokeBot.Core.Features.Guessing.Replies;
using BlokeBot.Persistence.Models;
using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlokeBot.Core.Tests;

/// <summary>
/// The staged redesign replaced thirteen reply text areas, a nine-checkbox whisper section, five
/// comma-grammar alias fields and a blank-means-forever pin box with narrated rows, chips and a
/// segmented choice. These cover the preservation claims that reading the markup does not settle:
/// that every message survives with its delivery choice, that the whisper rule still gates and
/// recovers, that the alias chips still write the comma model the validator consumes, and that the
/// explicit pin choice still stores null for until-stream-end.
/// </summary>
public sealed class GuessingSettingsStudioUiTests
{
    private static readonly IReadOnlyList<string> _chatOnlyReplies =
    [
        "round-started",
        "guessing-stopped",
        "winner-announced",
        "no-winners",
    ];

    [Test]
    public async Task Replies_KeepEveryMessageWithItsDeliveryChoiceOrChatOnlyNote()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedAsync(dbFactory);
        await using var context = CreateContext(dbFactory, hostId);
        var page = context.Render<GuessingSettings>();
        var defaults = GuessingDefaults.Replies();

        var rows = page.FindAll("[data-reply]");

        rows.Select(static row => row.GetAttribute("data-reply"))
            .ShouldBe([
                "round-started",
                "round-already-running",
                "no-round-running",
                "guessing-stopped",
                "guessing-already-stopped",
                "guessing-closed",
                "invalid-guess",
                "how-to-guess",
                "available-guesses",
                "how-to-choose-a-winner",
                "only-moderators",
                "winner-announced",
                "no-winners",
            ]);
        page.FindAll("[data-reply] .studio-reply__excerpt")
            .Select(static excerpt => excerpt.TextContent)
            .ShouldBe([
                defaults.RoundStartedReply,
                defaults.RoundAlreadyOpenReply,
                defaults.NoOpenRoundReply,
                defaults.GuessingStoppedReply,
                defaults.GuessingAlreadyStoppedReply,
                defaults.GuessingClosedReply,
                defaults.InvalidGuessReply,
                defaults.GuessUsageReply,
                defaults.AvailableGuessesReply,
                defaults.WinUsageReply,
                defaults.ModeratorOnlyReply,
                defaults.WinnerReply,
                defaults.NoWinnersReply,
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
            .Count.ShouldBe(GuessingReplyKeys.WhisperableKeys.Count);
        page.FindAll("[data-reply] textarea").Count.ShouldBe(13);
    }

    [Test]
    public async Task WhisperWithoutACustomBot_DisablesEveryDeliveryChoiceAndOffersRecovery()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedAsync(dbFactory);
        await using var context = CreateContext(dbFactory, hostId);
        var page = context.Render<GuessingSettings>();

        OpenEveryReply(page);

        page.FindAll(".studio-segmented__option")
            .ShouldAllBe(static option => option.HasAttribute("disabled"));
        page.FindAll("a[href='/host#custom-bot']").Count.ShouldBe(2);
        page.Find("#answer-replies-whisper-recovery")
            .TextContent.ShouldContain("need a connected custom bot");
        page.Find("#reply-delivery-whisper-recovery")
            .TextContent.ShouldContain("need a connected custom bot");
    }

    [Test]
    public async Task WhisperWithACustomBot_ChoosingWhisperOnAReply_PersistsThatDelivery()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedAsync(dbFactory, whisperCapable: true);
        await using var context = CreateContext(dbFactory, hostId);
        var page = context.Render<GuessingSettings>();
        page.Find("[data-reply='invalid-guess'] .studio-reply__header").Click();

        page.FindAll("[data-reply='invalid-guess'] .studio-segmented__option")
            .Single(static option => option.TextContent == "Whisper")
            .Click();

        page.Find("[data-reply='invalid-guess'] .studio-reply__state")
            .TextContent.ShouldBe("whisper");
        page.FindAll("button")
            .Single(static button => button.TextContent.Trim() == "Save changes")
            .Click();
        page.WaitForAssertion(() =>
            page.Find("[data-reply='invalid-guess'] .studio-reply__state")
                .TextContent.ShouldBe("whisper")
        );
        await using var db = await dbFactory.CreateDbContextAsync();
        var whispered = await db
            .ReplyDeliverySettings.Where(setting =>
                setting.HostId == hostId && setting.Target == ReplyDeliveryTarget.Whisper
            )
            .Select(setting => setting.ReplyKey)
            .ToListAsync();
        whispered.ShouldBe([GuessingReplyKeys.InvalidGuess]);
    }

    [Test]
    public async Task CommandWords_AddedAndRemovedAsChips_PersistTheChannelCommandWords()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedAsync(dbFactory);
        await using var context = CreateContext(dbFactory, hostId);
        var page = context.Render<GuessingSettings>();

        page.Find("#guessing-start-aliases").Input("gogogo");
        page.Find("#guessing-start-aliases").KeyDown("Enter");
        page.FindAll("[data-action='remove-command-word']")
            .Single(static button =>
                button.GetAttribute("aria-label") == "Remove !start from Start round"
            )
            .Click();
        page.FindAll("button")
            .Single(static button => button.TextContent.Trim() == "Save changes")
            .Click();

        await using var db = await dbFactory.CreateDbContextAsync();
        page.WaitForAssertion(() =>
            page.FindAll(
                    "[data-action='remove-command-word'][aria-label='Remove !gogogo from Start round']"
                )
                .Count.ShouldBe(1)
        );
        var startWords = await db
            .CommandAliases.Where(alias =>
                alias.HostId == hostId && alias.Kind == AppCommandKind.Start
            )
            .Select(alias => alias.Alias)
            .OrderBy(alias => alias)
            .ToListAsync();
        startWords.ShouldBe(["gogogo", "startguessing"]);
    }

    [Test]
    public async Task PinChoice_SwitchingBetweenSetTimeAndStreamEnd_StoresTheDurationOrNull()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedAsync(dbFactory);
        await using var context = CreateContext(dbFactory, hostId);
        var page = context.Render<GuessingSettings>();
        page.Find("[aria-label='Pin round-start replies']").Click();

        page.FindAll("[data-stage='round-start-pin'] .studio-segmented__option")
            .Single(static option => option.TextContent == "For a set time")
            .Click();
        Save(page);

        page.WaitForAssertion(() =>
            page.Find("[data-stage='round-start-pin'] .studio-stepper__value")
                .GetAttribute("value")
                .ShouldBe("300")
        );
        (await PinDurationAsync(dbFactory, hostId)).ShouldBe(300);

        page.FindAll("[data-stage='round-start-pin'] .studio-segmented__option")
            .Single(static option => option.TextContent == "Until the stream ends")
            .Click();
        page.FindAll("[data-stage='round-start-pin'] .studio-stepper").ShouldBeEmpty();
        Save(page);

        page.WaitForAssertion(() =>
            page.FindAll("[data-stage='round-start-pin'] .studio-stepper").ShouldBeEmpty()
        );
        (await PinDurationAsync(dbFactory, hostId)).ShouldBeNull();
    }

    private static void Save(IRenderedComponent<GuessingSettings> page) =>
        page.FindAll("button")
            .Single(static button => button.TextContent.Trim() == "Save changes")
            .Click();

    private static void OpenEveryReply(IRenderedComponent<GuessingSettings> page)
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

    private static async Task<int?> PinDurationAsync(SqliteBlokeBotDbFactory dbFactory, int hostId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var policy = await db.ReplyPinPolicies.SingleAsync(candidate => candidate.HostId == hostId);
        return policy.DurationSeconds;
    }

    private static BunitContext CreateContext(SqliteBlokeBotDbFactory dbFactory, int hostId)
    {
        var context = UiTestContextFactory.Create(dbFactory, hostId);
        _ = context.Services.AddSingleton<GuessingChangeNotifier>();
        _ = context.Services.AddSingleton<GuessingConfigurationService>();
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
            EnabledFeatures = HostFeatureFlags.Guessing,
            CreatedAtUtc = DateTime.UtcNow,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        var profile = new GuessRoundProfile
        {
            HostId = host.Id,
            Name = "Default",
            Slug = "default",
            IsDefault = true,
            ReplySettings = ReplySettingsMapper.ToEntity(GuessingDefaults.Replies()),
            Options = [new GuessOption { Name = "blue", ReplyText = "Blue" }],
        };
        _ = db.Profiles.Add(profile);
        _ = await db.SaveChangesAsync();
        db.CommandAliases.AddRange(
            new CommandAlias
            {
                HostId = host.Id,
                GuessRoundProfileId = profile.Id,
                Kind = AppCommandKind.Start,
                Alias = "startguessing",
            },
            new CommandAlias
            {
                HostId = host.Id,
                GuessRoundProfileId = profile.Id,
                Kind = AppCommandKind.Start,
                Alias = "start",
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
