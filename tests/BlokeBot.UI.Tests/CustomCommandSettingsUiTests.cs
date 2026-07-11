using BlokeBot.Features.CustomCommands;
using BlokeBot.Features.Toasts;
using BlokeBot.Persistence.Models;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.UI.Tests;

public sealed class CustomCommandSettingsUiTests
{
    [Test]
    public async Task ActionAndScheduleVariants_ChangingSelections_ShowsMatchingControls()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var seeded = await SeedConfigurationAsync(dbFactory);
        await using var context = UiTestContextFactory.Create(dbFactory, seeded.HostId);

        var cut = context.Render<CustomCommandSettingsPage>();

        cut.Markup.ShouldContain("Always use the first message");
        cut.Markup.ShouldContain("Everyone shares the wait");
        cut.Markup.ShouldContain("Add 1 to a counter, then send a reply");
        cut.Markup.ShouldContain("Message 1");
        cut.Markup.ShouldNotContain("Message library");
        cut.Markup.ShouldNotContain("Rotation index");
        cut.Markup.ShouldNotContain("Action type");
        cut.Find($"#command-{seeded.CommandId}-counter-id");
        cut.Find("button[aria-controls='custom-announcement-settings']").Click();
        cut.Markup.ShouldContain("On a timer, after chat activity");
        cut.Markup.ShouldNotContain("Schedule type");
        cut.Find($"#announcement-{seeded.AnnouncementId}-required-chat-messages");
        var actionSelect = cut.Find($"#command-{seeded.CommandId}-action-kind");
        actionSelect.Change(CustomCommandActionKind.Message.ToString());

        cut.FindAll($"#command-{seeded.CommandId}-counter-id").ShouldBeEmpty();
    }

    [Test]
    public async Task InvalidCustomCommandEditor_Saving_ShowsErrorWithoutPersistence()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var seeded = await SeedConfigurationAsync(dbFactory);
        await using var context = UiTestContextFactory.Create(dbFactory, seeded.HostId);
        var toasts = context.Services.GetRequiredService<ToastService>();
        var cut = context.Render<CustomCommandSettingsPage>();
        cut.Find($"#message-entry-{seeded.MessageEntryId}-name").Input(string.Empty);

        cut.Find("button[aria-label='Save custom commands']").Click();

        var error = toasts.Current.Single();
        error.Kind.ShouldBe(ToastKind.Error);
        error.Message.ShouldBe("Reply name is required.");
        await using var db = await dbFactory.CreateDbContextAsync();
        (await db.CustomMessageLibraryEntries.FindAsync(seeded.MessageEntryId))!
            .Name.ShouldBe("Message");
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

    private sealed record SeededConfiguration(
        int HostId,
        int MessageEntryId,
        int CommandId,
        int AnnouncementId
    );
}
