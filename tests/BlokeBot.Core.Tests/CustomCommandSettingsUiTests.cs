using BlokeBot.Announcements;
using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Core.Features.Toasts;
using BlokeBot.Persistence.Models;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Core.Tests;

public sealed class CustomCommandSettingsUiTests
{
    [Test]
    public async Task ActionKind_ChangingToMessage_HidesCounterControl()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var seeded = await SeedConfigurationAsync(dbFactory);
        await using var context = UiTestContextFactory.Create(dbFactory, seeded.HostId);

        var cut = context.Render<CustomCommandSettingsPage>();

        var actionSelect = cut.Find($"#command-{seeded.CommandId}-action-kind");
        var reply = cut.Find($"#command-{seeded.CommandId}-reply");
        reply.GetAttribute("aria-invalid").ShouldBeNull();
        reply.GetAttribute("aria-describedby").ShouldBeNull();
        actionSelect.Change(CustomCommandActionKind.Message.ToString());

        cut.FindAll($"#command-{seeded.CommandId}-counter-id").ShouldBeEmpty();
    }

    [Test]
    public async Task NewAnnouncement_Adding_UsesValidDeliveryTimingDefaults()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var seeded = await SeedConfigurationAsync(dbFactory);
        await using var context = UiTestContextFactory.Create(dbFactory, seeded.HostId);
        var toasts = context.Services.GetRequiredService<ToastService>();
        var cut = context.Render<CustomCommandSettingsPage>();
        cut.Find("button[aria-controls='custom-announcement-settings']").Click();

        cut.Find("button[data-action='add-scheduled-message']").Click();

        cut.Find("#announcement--1-retry-delay").GetAttribute("value").ShouldBe("2");
        cut.Find("#announcement--1-occurrence-lifetime").GetAttribute("value").ShouldBe("30");

        cut.Find("button[aria-label='Save custom commands']").Click();

        cut.FindAll("[data-validation-summary]").ShouldBeEmpty();
        toasts.Current.ShouldHaveSingleItem().Kind.ShouldBe(ToastKind.Success);
        await using var db = await dbFactory.CreateDbContextAsync();
        (await db.CustomAnnouncements.CountAsync()).ShouldBe(2);
    }

    [Test]
    public async Task InvalidCustomCommandEditor_Saving_ShowsAllErrorsUntilCorrected()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var seeded = await SeedConfigurationAsync(dbFactory);
        await using var context = UiTestContextFactory.Create(dbFactory, seeded.HostId);
        var toasts = context.Services.GetRequiredService<ToastService>();
        var cut = context.Render<CustomCommandSettingsPage>();
        cut.Find("#custom-command-message-library-tab").Click();
        cut.Find("textarea").Input(string.Empty);
        cut.Find("#custom-command-commands-tab").Click();
        cut.Find("button[aria-controls='custom-announcement-settings']").Click();
        cut.Find($"#announcement-{seeded.AnnouncementId}-retry-delay").Change("0");
        cut.Find($"#announcement-{seeded.AnnouncementId}-occurrence-lifetime").Change("61");

        cut.Find("button[aria-label='Save custom commands']").Click();

        ValidationMessages(cut).Length.ShouldBe(3);
        cut.Find("#custom-command-message-library-tab")
            .GetAttribute("aria-selected")
            .ShouldBe("true");
        var invalidMessage = cut.Find("textarea");
        invalidMessage.GetAttribute("aria-invalid").ShouldBe("true");
        invalidMessage.GetAttribute("aria-describedby").ShouldNotBeNull();
        cut.Find("#custom-command-commands-tab").Click();
        var invalidRetry = cut.Find($"#announcement-{seeded.AnnouncementId}-retry-delay");
        invalidRetry.GetAttribute("aria-invalid").ShouldBe("true");
        invalidRetry
            .GetAttribute("aria-describedby")
            .ShouldBe($"announcement-{seeded.AnnouncementId}-retry-delay-error");
        toasts.Current.ShouldHaveSingleItem().Kind.ShouldBe(ToastKind.Error);
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            (await db.CustomMessageLibraryEntries.FindAsync(seeded.MessageEntryId))!.Name.ShouldBe(
                "Message"
            );
        }

        cut.Find("textarea").Input("Corrected reply");
        cut.Find("button[aria-label='Save custom commands']").Click();

        ValidationMessages(cut).Length.ShouldBe(2);

        cut.Find($"#announcement-{seeded.AnnouncementId}-retry-delay").Change("2");
        cut.Find($"#announcement-{seeded.AnnouncementId}-occurrence-lifetime").Change("30");
        cut.Find("button[aria-label='Save custom commands']").Click();

        cut.FindAll("[data-validation-summary]").ShouldBeEmpty();
        var success = toasts.Current.Last();
        success.Kind.ShouldBe(ToastKind.Success);
        await using var savedDb = await dbFactory.CreateDbContextAsync();
        (
            await savedDb
                .CustomMessageLibraryEntries.Include(entry => entry.Variants)
                .SingleAsync(entry => entry.Id == seeded.MessageEntryId)
        )
            .Variants.Single()
            .Text.ShouldBe("Corrected reply");
    }

    [Test]
    public async Task BuiltInAliasCollision_Saving_ActivatesCommandsAndAssociatesTheMatchingAlias()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var seeded = await SeedConfigurationAsync(dbFactory);
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            db.CommandAliases.Add(
                new CommandAlias
                {
                    HostId = seeded.HostId,
                    Kind = AppCommandKind.Points,
                    Alias = "points",
                }
            );
            await db.SaveChangesAsync();
        }

        await using var context = UiTestContextFactory.Create(dbFactory, seeded.HostId);
        var toasts = context.Services.GetRequiredService<ToastService>();
        var cut = context.Render<CustomCommandSettingsPage>();

        cut.Find($"#command-{seeded.CommandId}-aliases").Input("points");
        cut.Find("button[aria-label='Save custom commands']").Click();

        cut.Find("#custom-command-commands-tab").GetAttribute("aria-selected").ShouldBe("true");
        var aliases = cut.Find($"#command-{seeded.CommandId}-aliases");
        aliases.GetAttribute("aria-invalid").ShouldBe("true");
        aliases.GetAttribute("aria-describedby").ShouldNotBeNull();
        ValidationMessages(cut).Length.ShouldBe(1);
        toasts.Current.ShouldHaveSingleItem().Kind.ShouldBe(ToastKind.Error);
        await using var savedDb = await dbFactory.CreateDbContextAsync();
        (await savedDb.CustomCommandAliases.SingleAsync()).Alias.ShouldBe("command");
    }

    [Test]
    public async Task SettingsTabs_ArrowHomeAndEndKeys_SelectAndFocusTheExpectedTab()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var seeded = await SeedConfigurationAsync(dbFactory);
        await using var context = UiTestContextFactory.Create(dbFactory, seeded.HostId);
        var cut = context.Render<CustomCommandSettingsPage>();

        cut.Find("#custom-command-commands-tab")
            .KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });

        cut.Find("#custom-command-message-library-tab")
            .GetAttribute("aria-selected")
            .ShouldBe("true");
        cut.Find("#custom-command-commands-panel").GetAttribute("hidden").ShouldNotBeNull();
        cut.Find("#custom-command-message-library-panel")
            .GetAttribute("aria-labelledby")
            .ShouldBe("custom-command-message-library-tab");
        cut.Find("#custom-command-message-library-panel").GetAttribute("hidden").ShouldBeNull();

        cut.Find("#custom-command-message-library-tab")
            .KeyDown(new KeyboardEventArgs { Key = "Home" });

        cut.Find("#custom-command-commands-tab").GetAttribute("aria-selected").ShouldBe("true");
        cut.Find("#custom-command-message-library-panel").GetAttribute("hidden").ShouldNotBeNull();
        cut.Find("#custom-command-commands-tab").KeyDown(new KeyboardEventArgs { Key = "End" });
        cut.Find("#custom-command-message-library-tab").GetAttribute("tabindex").ShouldBe("0");
    }

    [Test]
    public async Task CommandsWithoutReplies_OffersDirectMessageLibraryAction()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedEmptyConfigurationAsync(dbFactory);
        await using var context = UiTestContextFactory.Create(dbFactory, hostId);
        var cut = context.Render<CustomCommandSettingsPage>();

        cut.Find("button[data-action='create-reply']").Click();

        cut.Find("#custom-command-message-library-tab")
            .GetAttribute("aria-selected")
            .ShouldBe("true");
        cut.Find("#custom-command-message-library-panel").ShouldNotBeNull();
    }

    private static string[] ValidationMessages(IRenderedComponent<CustomCommandSettingsPage> page)
    {
        var summary = page.Find("[data-validation-summary]");
        var title = summary.QuerySelector("#custom-command-validation-title");
        title.ShouldNotBeNull();
        title.TextContent.Trim().ShouldBe("Check these settings");
        return summary.QuerySelectorAll("li").Select(item => item.TextContent.Trim()).ToArray();
    }

    private static async Task<SeededConfiguration> SeedConfigurationAsync(
        SqliteBlokeBotDbFactory dbFactory
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var host = new BotHost
        {
            Login = "streamer",
            DisplayName = "Streamer",
            EnabledFeatures = HostFeatureFlags.All,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.Hosts.Add(host);
        await db.SaveChangesAsync();
        var entry = new CustomMessageLibraryEntry
        {
            HostId = host.Id,
            Name = "Message",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            Variants = [new CustomMessageVariant { SortOrder = 0, Text = "Message text" }],
        };
        var counter = new CustomCounter
        {
            HostId = host.Id,
            Name = "Count",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };
        db.AddRange(entry, counter);
        await db.SaveChangesAsync();
        var command = new CustomCommand
        {
            HostId = host.Id,
            Name = "Command",
            Action = new CounterCustomCommandAction
            {
                HostId = host.Id,
                MessageLibraryEntryId = entry.Id,
                CounterId = counter.Id,
            },
            Aliases = [new CustomCommandAlias { HostId = host.Id, Alias = "command" }],
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };
        var announcement = new CustomAnnouncement
        {
            HostId = host.Id,
            Name = "Announcement",
            MessageLibraryEntryId = entry.Id,
            DeliveryPolicy = new RetryUntilExpiredThenSkipCustomAnnouncementDeliveryPolicy
            {
                HostId = host.Id,
                RetryDelay = new AnnouncementRetryDelay(TimeSpan.FromSeconds(2)),
                OccurrenceLifetime = new AnnouncementOccurrenceLifetime(TimeSpan.FromSeconds(30)),
            },
            Schedule = new IntervalAfterChatCustomAnnouncementSchedule
            {
                HostId = host.Id,
                IntervalMinutes = 30,
                RequiredChatMessages = 3,
            },
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };
        db.AddRange(command, announcement);
        await db.SaveChangesAsync();
        return new SeededConfiguration(host.Id, entry.Id, command.Id, announcement.Id);
    }

    private static async Task<int> SeedEmptyConfigurationAsync(SqliteBlokeBotDbFactory dbFactory)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var host = new BotHost
        {
            Login = "streamer",
            DisplayName = "Streamer",
            EnabledFeatures = HostFeatureFlags.All,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.Hosts.Add(host);
        await db.SaveChangesAsync();
        return host.Id;
    }

    private sealed record SeededConfiguration(
        int HostId,
        int MessageEntryId,
        int CommandId,
        int AnnouncementId
    );
}
