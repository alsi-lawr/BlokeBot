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
    public async Task CompleteCustomCommandDraft_SavingThenLoading_RoundTripsConfiguration()
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
                            new CustomMessageVariantEditor { Id = -2, Text = "Hi {user}." },
                            new CustomMessageVariantEditor { Id = -3, Text = "Hello {channel}." },
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
                        Action = new CounterCustomCommandActionEditor
                        {
                            MessageLibraryEntryId = -1,
                            CounterId = -4,
                        },
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
                        RetryDelaySeconds = 3,
                        OccurrenceLifetimeSeconds = 45,
                        Schedule = new WeeklyCustomAnnouncementScheduleEditor
                        {
                            Day = DayOfWeek.Friday,
                            Time = new TimeOnly(19, 30),
                        },
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
        var action = command.Action.ShouldBeOfType<CounterCustomCommandActionEditor>();
        action.MessageLibraryEntryId.ShouldBe(entry.Id);
        action.CounterId.ShouldBe(counter.Id);

        var announcement = loaded.Announcements.Single();
        announcement.Name.ShouldBe("Reminder");
        announcement.Enabled.ShouldBeFalse();
        announcement.MessageLibraryEntryId.ShouldBe(entry.Id);
        announcement.RetryDelaySeconds.ShouldBe(3);
        announcement.OccurrenceLifetimeSeconds.ShouldBe(45);
        var schedule =
            announcement.Schedule.ShouldBeOfType<WeeklyCustomAnnouncementScheduleEditor>();
        schedule.Day.ShouldBe(DayOfWeek.Friday);
        schedule.Time.ShouldBe(new TimeOnly(19, 30));
    }

    [Test]
    public async Task LoadedAliasesAndMessageVariants_MutatingBeforeSave_IsIsolatedThenPersistsOnSave()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var service = CreateService(dbFactory);
        await service.SaveConfigurationAsync(
            hostId,
            ConfigurationWithCommands(("Command", "original")),
            CancellationToken.None
        );
        var editor = await service.LoadConfigurationAsync(hostId, CancellationToken.None);
        editor.Commands.Single().Aliases = "updated";
        editor.MessageEntries.Single().Variants.Single().Text = "Updated reply.";

        var beforeSave = await service.LoadConfigurationAsync(hostId, CancellationToken.None);

        beforeSave.Commands.Single().Aliases.ShouldBe("original");
        beforeSave.MessageEntries.Single().Variants.Single().Text.ShouldBe("Reply text.");

        await service.SaveConfigurationAsync(hostId, editor, CancellationToken.None);

        var afterSave = await service.LoadConfigurationAsync(hostId, CancellationToken.None);
        afterSave.Commands.Single().Aliases.ShouldBe("updated");
        afterSave.MessageEntries.Single().Variants.Single().Text.ShouldBe("Updated reply.");
    }

    [Test]
    public async Task BuiltInOrDuplicateDraftAlias_Saving_RejectsCollision()
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
        builtInCollision.Message.ShouldContain("another bot command");

        var draftCollision = await Should.ThrowAsync<InvalidOperationException>(() =>
            service.SaveConfigurationAsync(
                hostId,
                ConfigurationWithCommands(("First", "hello"), ("Second", "!HELLO")),
                CancellationToken.None
            )
        );
        draftCollision.Message.ShouldContain("another custom command");
    }

    [Test]
    public async Task ActionAndScheduleTypeChanges_Saving_ReplacesVariantsAndAliases()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var service = CreateService(dbFactory);
        var draft = ConfigurationWithCommands(("Command", "old-alias"));
        draft.Counters.Add(new CustomCounterEditor { Id = -10, Name = "Count" });
        draft.Announcements.Add(
            new CustomAnnouncementEditor
            {
                Id = -11,
                Name = "Announcement",
                MessageLibraryEntryId = -1,
                RetryDelaySeconds = 2,
                OccurrenceLifetimeSeconds = 30,
            }
        );
        await service.SaveConfigurationAsync(hostId, draft, CancellationToken.None);
        var update = await service.LoadConfigurationAsync(hostId, CancellationToken.None);
        var command = update.Commands.Single();
        command.Aliases = "new-alias";
        command.ActionKind = CustomCommandActionKind.Counter;
        command.Action.ShouldBeOfType<CounterCustomCommandActionEditor>().CounterId = update
            .Counters.Single()
            .Id;
        var announcement = update.Announcements.Single();
        announcement.ScheduleKind = CustomAnnouncementScheduleKind.IntervalAfterChat;
        var intervalAfterChat =
            announcement.Schedule.ShouldBeOfType<IntervalAfterChatCustomAnnouncementScheduleEditor>();
        intervalAfterChat.IntervalMinutes = 20;
        intervalAfterChat.RequiredChatMessages = 4;

        await service.SaveConfigurationAsync(hostId, update, CancellationToken.None);

        var loaded = await service.LoadConfigurationAsync(hostId, CancellationToken.None);
        loaded.Commands.Single().Aliases.ShouldBe("new-alias");
        loaded.Commands.Single().Action.ShouldBeOfType<CounterCustomCommandActionEditor>();
        var loadedSchedule = loaded
            .Announcements.Single()
            .Schedule.ShouldBeOfType<IntervalAfterChatCustomAnnouncementScheduleEditor>();
        loadedSchedule.IntervalMinutes.ShouldBe(20);
        loadedSchedule.RequiredChatMessages.ShouldBe(4);
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            (await db.CustomCommandAliases.AnyAsync(x => x.Alias == "old-alias")).ShouldBeFalse();
        }

        loaded.Commands.Single().ActionKind = CustomCommandActionKind.Message;
        loaded.Announcements.Single().ScheduleKind = CustomAnnouncementScheduleKind.Weekly;
        var weekly = loaded
            .Announcements.Single()
            .Schedule.ShouldBeOfType<WeeklyCustomAnnouncementScheduleEditor>();
        weekly.Day = DayOfWeek.Sunday;
        weekly.Time = new TimeOnly(9, 15);
        await service.SaveConfigurationAsync(hostId, loaded, CancellationToken.None);

        var final = await service.LoadConfigurationAsync(hostId, CancellationToken.None);
        final.Commands.Single().Action.ShouldBeOfType<MessageCustomCommandActionEditor>();
        var finalSchedule = final
            .Announcements.Single()
            .Schedule.ShouldBeOfType<WeeklyCustomAnnouncementScheduleEditor>();
        finalSchedule.Day.ShouldBe(DayOfWeek.Sunday);
        finalSchedule.Time.ShouldBe(new TimeOnly(9, 15));
    }

    [Test]
    public async Task RemovedConfigurationGraph_Saving_DeletesOwnedRowsAndAliases()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var service = CreateService(dbFactory);
        var draft = ConfigurationWithCommands(("Command", "command"));
        draft.Counters.Add(new CustomCounterEditor { Id = -10, Name = "Counter" });
        draft.Announcements.Add(
            new CustomAnnouncementEditor
            {
                Id = -11,
                Name = "Announcement",
                MessageLibraryEntryId = -1,
                RetryDelaySeconds = 2,
                OccurrenceLifetimeSeconds = 30,
            }
        );
        await service.SaveConfigurationAsync(hostId, draft, CancellationToken.None);
        var update = await service.LoadConfigurationAsync(hostId, CancellationToken.None);
        update.Commands.Clear();
        update.Announcements.Clear();
        update.Counters.Clear();
        update.MessageEntries.Clear();

        await service.SaveConfigurationAsync(hostId, update, CancellationToken.None);

        await using var db = await dbFactory.CreateDbContextAsync();
        (await db.CustomCommands.CountAsync()).ShouldBe(0);
        (await db.CustomCommandActions.CountAsync()).ShouldBe(0);
        (await db.CustomCommandAliases.CountAsync()).ShouldBe(0);
        (await db.CustomAnnouncements.CountAsync()).ShouldBe(0);
        (await db.CustomAnnouncementSchedules.CountAsync()).ShouldBe(0);
        (await db.CustomAnnouncementDeliveryPolicies.CountAsync()).ShouldBe(0);
        (await db.CustomCounters.CountAsync()).ShouldBe(0);
        (await db.CustomMessageLibraryEntries.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task InvalidOrCrossHostGraphReferences_Saving_RejectsWithoutMutation()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var firstHostId = await SeedHostAsync(dbFactory, "first");
        var secondHostId = await SeedHostAsync(dbFactory, "second");
        var service = CreateService(dbFactory);
        await service.SaveConfigurationAsync(
            firstHostId,
            ConfigurationWithCommands(("Command", "command")),
            CancellationToken.None
        );
        var stored = await service.LoadConfigurationAsync(firstHostId, CancellationToken.None);

        var missingId = await service.LoadConfigurationAsync(firstHostId, CancellationToken.None);
        missingId.Commands.Single().Id = 999_999;
        var missingError = await Should.ThrowAsync<InvalidOperationException>(() =>
            service.SaveConfigurationAsync(firstHostId, missingId, CancellationToken.None)
        );
        missingError.Message.ShouldContain("no longer available");

        var invalidMessage = ConfigurationWithCommands(("Invalid", "invalid"));
        invalidMessage.Commands.Single().Action.MessageLibraryEntryId = -999;
        var messageError = await Should.ThrowAsync<InvalidOperationException>(() =>
            service.SaveConfigurationAsync(firstHostId, invalidMessage, CancellationToken.None)
        );
        messageError.Message.ShouldContain("Choose a saved reply");

        var invalidCounter = ConfigurationWithCommands(("Counter", "counter"));
        invalidCounter.Commands.Single().Action = new CounterCustomCommandActionEditor
        {
            MessageLibraryEntryId = -1,
            CounterId = -999,
        };
        var counterError = await Should.ThrowAsync<InvalidOperationException>(() =>
            service.SaveConfigurationAsync(firstHostId, invalidCounter, CancellationToken.None)
        );
        counterError.Message.ShouldContain("Choose a counter");

        var hostBoundaryError = await Should.ThrowAsync<InvalidOperationException>(() =>
            service.SaveConfigurationAsync(secondHostId, stored, CancellationToken.None)
        );
        hostBoundaryError.Message.ShouldContain("no longer available");

        var unchanged = await service.LoadConfigurationAsync(firstHostId, CancellationToken.None);
        unchanged.Commands.Single().Name.ShouldBe("Command");
        unchanged.Commands.Single().Aliases.ShouldBe("command");
    }

    [Test]
    public async Task InvalidAnnouncementSchedule_Saving_RejectsVariant()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var service = CreateService(dbFactory);
        var interval = ConfigurationWithAnnouncement(
            new IntervalCustomAnnouncementScheduleEditor { IntervalMinutes = 0 }
        );
        var intervalError = await Should.ThrowAsync<InvalidOperationException>(() =>
            service.SaveConfigurationAsync(hostId, interval, CancellationToken.None)
        );
        intervalError.Message.ShouldContain("at least 1");

        var afterChat = ConfigurationWithAnnouncement(
            new IntervalAfterChatCustomAnnouncementScheduleEditor
            {
                IntervalMinutes = 10,
                RequiredChatMessages = 0,
            }
        );
        var afterChatError = await Should.ThrowAsync<InvalidOperationException>(() =>
            service.SaveConfigurationAsync(hostId, afterChat, CancellationToken.None)
        );
        afterChatError.Message.ShouldContain("at least 1 chat message");
    }

    [Test]
    public async Task InvalidAnnouncementDeliveryTiming_Saving_RejectsWithoutPersistence()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var service = CreateService(dbFactory);
        var missing = ConfigurationWithAnnouncement(new IntervalCustomAnnouncementScheduleEditor());
        missing.Announcements.Single().RetryDelaySeconds = 0;
        var missingError = await Should.ThrowAsync<InvalidOperationException>(() =>
            service.SaveConfigurationAsync(hostId, missing, CancellationToken.None)
        );
        missingError.Message.ShouldContain("retry delay must be positive");

        var excessive = ConfigurationWithAnnouncement(
            new IntervalCustomAnnouncementScheduleEditor()
        );
        excessive.Announcements.Single().OccurrenceLifetimeSeconds = 61;
        var excessiveError = await Should.ThrowAsync<InvalidOperationException>(() =>
            service.SaveConfigurationAsync(hostId, excessive, CancellationToken.None)
        );
        excessiveError.Message.ShouldContain("no greater than 60");

        var inconsistent = ConfigurationWithAnnouncement(
            new IntervalCustomAnnouncementScheduleEditor()
        );
        inconsistent.Announcements.Single().RetryDelaySeconds = 30;
        inconsistent.Announcements.Single().OccurrenceLifetimeSeconds = 30;
        var inconsistentError = await Should.ThrowAsync<InvalidOperationException>(() =>
            service.SaveConfigurationAsync(hostId, inconsistent, CancellationToken.None)
        );
        inconsistentError.Message.ShouldContain("less than its occurrence lifetime");

        await using var db = await dbFactory.CreateDbContextAsync();
        (await db.CustomAnnouncements.CountAsync()).ShouldBe(0);
        (await db.CustomAnnouncementDeliveryPolicies.CountAsync()).ShouldBe(0);
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
                    Variants = [new CustomMessageVariantEditor { Id = -2, Text = "Reply text." }],
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
                    Action = new MessageCustomCommandActionEditor { MessageLibraryEntryId = -1 },
                }
            );
        }

        return config;
    }

    private static CustomCommandConfiguration ConfigurationWithAnnouncement(
        ICustomAnnouncementScheduleEditor schedule
    )
    {
        var config = ConfigurationWithCommands();
        config.Announcements.Add(
            new CustomAnnouncementEditor
            {
                Id = -3,
                Name = "Announcement",
                MessageLibraryEntryId = -1,
                RetryDelaySeconds = 2,
                OccurrenceLifetimeSeconds = 30,
                Schedule = schedule,
            }
        );
        return config;
    }

    private static CustomCommandConfigurationService CreateService(
        SqliteBlokeBotDbFactory dbFactory
    )
    {
        var events = TestEventBus.Create<AppEventKind>();
        return new CustomCommandConfigurationService(
            dbFactory,
            new CustomCommandAliasRegistry(),
            new CustomCommandConfigurationGraphWriter(dbFactory, TimeProvider.System),
            new HostCustomCommandSettingsService(dbFactory, events),
            events
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
}
