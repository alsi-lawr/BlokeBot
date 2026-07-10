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
    public async Task Variant_controls_follow_selected_action_and_schedule_types()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var seeded = await SeedConfigurationAsync(dbFactory);
        await using var context = UiTestContextFactory.Create(dbFactory, seeded.HostId);

        var cut = context.Render<CustomCommandSettingsPage>();

        cut.Find($"#command-{seeded.CommandId}-counter-id");
        cut.Find("button[aria-controls='custom-announcement-settings']").Click();
        cut.Find($"#announcement-{seeded.AnnouncementId}-required-chat-messages");
        var actionSelect = cut.Find($"#command-{seeded.CommandId}-action-kind");
        actionSelect.Change(CustomCommandActionKind.Message.ToString());

        cut.FindAll($"#command-{seeded.CommandId}-counter-id").ShouldBeEmpty();
    }

    [Test]
    public async Task Invalid_editor_state_surfaces_validation_error_without_persisting()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var seeded = await SeedConfigurationAsync(dbFactory);
        await using var context = UiTestContextFactory.Create(dbFactory, seeded.HostId);
        var toasts = context.Services.GetRequiredService<ToastService>();
        var cut = context.Render<CustomCommandSettingsPage>();
        cut.Find($"#message-entry-{seeded.MessageEntryId}-name").Input(string.Empty);

        cut.Find("button[aria-label='Save custom command settings']").Click();

        var error = toasts.Current.Single();
        error.Kind.ShouldBe(ToastKind.Error);
        error.Message.ShouldNotBeNullOrWhiteSpace();
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
