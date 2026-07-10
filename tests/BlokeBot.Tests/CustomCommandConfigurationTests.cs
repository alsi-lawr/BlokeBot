using BlokeBot.Eventing;
using BlokeBot.Features.CustomCommands;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Tests;

public sealed class CustomCommandConfigurationTests
{
    [Test]
    public async Task Saves_and_loads_custom_command_configuration()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var service = CreateService(dbFactory);

        await service.SaveConfigurationAsync(
            hostId,
            new CustomCommandConfiguration
            {
                TimeZoneId = "UTC",
                MessageEntries =
                [
                    new CustomMessageLibraryEntryEditor
                    {
                        Id = -1,
                        Name = "Greeting",
                        SelectionMode = CustomMessageSelectionMode.Sequential,
                        CurrentVariantIndex = 1,
                        Variants =
                        [
                            new CustomMessageVariantEditor
                            {
                                Id = -2,
                                Text = "Hi {user}.",
                            },
                            new CustomMessageVariantEditor
                            {
                                Id = -3,
                                Text = "Hello {channel}.",
                            },
                        ],
                    },
                ],
                Counters =
                [
                    new CustomCounterEditor
                    {
                        Id = -4,
                        Name = "Deaths",
                        Value = 5,
                    },
                ],
                Commands =
                [
                    new CustomCommandEditor
                    {
                        Id = -5,
                        Name = "Hello",
                        Aliases = "!HI, hello",
                        Enabled = false,
                        ModeratorOnly = true,
                        CooldownSeconds = 12,
                        CooldownScope = CustomCommandCooldownScope.User,
                        ActionType = CustomCommandActionType.Counter,
                        MessageLibraryEntryId = -1,
                        CounterId = -4,
                    },
                ],
                Announcements =
                [
                    new CustomAnnouncementEditor
                    {
                        Id = -6,
                        Name = "Reminder",
                        Enabled = false,
                        MessageLibraryEntryId = -1,
                        ScheduleType = CustomAnnouncementScheduleType.Weekly,
                        IntervalMinutes = 45,
                        RequiredChatMessages = 3,
                        WeeklyDay = DayOfWeek.Friday,
                        WeeklyTime = "19:30",
                    },
                ],
            },
            CancellationToken.None
        );

        var loaded = await service.LoadConfigurationAsync(hostId, CancellationToken.None);

        loaded.TimeZoneId.ShouldBe("UTC");
        var entry = loaded.MessageEntries.Single();
        entry.Name.ShouldBe("Greeting");
        entry.SelectionMode.ShouldBe(CustomMessageSelectionMode.Sequential);
        entry.CurrentVariantIndex.ShouldBe(1);
        entry.Variants.Select(x => x.Text).ShouldBe(["Hi {user}.", "Hello {channel}."]);

        var counter = loaded.Counters.Single();
        counter.Name.ShouldBe("Deaths");
        counter.Value.ShouldBe(5);

        var command = loaded.Commands.Single();
        command.Name.ShouldBe("Hello");
        command.Aliases.ShouldBe("hello, hi");
        command.Enabled.ShouldBeFalse();
        command.ModeratorOnly.ShouldBeTrue();
        command.CooldownSeconds.ShouldBe(12);
        command.CooldownScope.ShouldBe(CustomCommandCooldownScope.User);
        command.ActionType.ShouldBe(CustomCommandActionType.Counter);
        command.MessageLibraryEntryId.ShouldBe(entry.Id);
        command.CounterId.ShouldBe(counter.Id);

        var announcement = loaded.Announcements.Single();
        announcement.Name.ShouldBe("Reminder");
        announcement.Enabled.ShouldBeFalse();
        announcement.MessageLibraryEntryId.ShouldBe(entry.Id);
        announcement.ScheduleType.ShouldBe(CustomAnnouncementScheduleType.Weekly);
        announcement.IntervalMinutes.ShouldBe(45);
        announcement.RequiredChatMessages.ShouldBe(3);
        announcement.WeeklyDay.ShouldBe(DayOfWeek.Friday);
        announcement.WeeklyTime.ShouldBe("19:30");
    }

    [Test]
    public async Task Save_rejects_builtin_and_draft_alias_collisions()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            db.CommandAliases.Add(
                new CommandAlias
                {
                    HostId = hostId,
                    Kind = AppCommandKind.Points,
                    Alias = "points",
                }
            );
            await db.SaveChangesAsync();
        }

        var service = CreateService(dbFactory);

        var builtInCollision = await Should.ThrowAsync<InvalidOperationException>(() =>
            service.SaveConfigurationAsync(
                hostId,
                ConfigurationWithCommands(("Built in", "points")),
                CancellationToken.None
            )
        );
        builtInCollision.Message.ShouldContain("another bot function");

        var draftCollision = await Should.ThrowAsync<InvalidOperationException>(() =>
            service.SaveConfigurationAsync(
                hostId,
                ConfigurationWithCommands(("First", "hello"), ("Second", "!HELLO")),
                CancellationToken.None
            )
        );
        draftCollision.Message.ShouldContain("another custom command");
    }

    private static CustomCommandConfiguration ConfigurationWithCommands(
        params (string Name, string Aliases)[] commands
    )
    {
        var config = new CustomCommandConfiguration
        {
            MessageEntries =
            [
                new CustomMessageLibraryEntryEditor
                {
                    Id = -1,
                    Name = "Reply",
                    Variants =
                    [
                        new CustomMessageVariantEditor
                        {
                            Id = -2,
                            Text = "Reply text.",
                        },
                    ],
                },
            ],
        };

        var nextId = -3;
        foreach (var command in commands)
        {
            config.Commands.Add(
                new CustomCommandEditor
                {
                    Id = nextId--,
                    Name = command.Name,
                    Aliases = command.Aliases,
                    MessageLibraryEntryId = -1,
                }
            );
        }

        return config;
    }

    private static CustomCommandConfigurationService CreateService(
        SqliteBlokeBotDbFactory dbFactory
    )
    {
        var events = new EventBus<AppEventKind>();
        return new CustomCommandConfigurationService(
            dbFactory,
            new CustomCommandAliasRegistry(),
            new HostCustomCommandSettingsService(dbFactory, events),
            events,
            TimeProvider.System
        );
    }

    private static async Task<int> SeedHostAsync(
        SqliteBlokeBotDbFactory dbFactory,
        string login
    )
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
}
