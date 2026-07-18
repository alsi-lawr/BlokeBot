using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Eventing;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Core.Tests;

public sealed class CustomCommandConfigurationTests
{
    [Test]
    public void NewAnnouncementEditor_Creating_UsesValidDeliveryTimingDefaults()
    {
        var announcement = new CustomAnnouncementEditor();

        announcement.RetryDelaySeconds.ShouldBe(2);
        announcement.OccurrenceLifetimeSeconds.ShouldBe(30);
    }

    [Test]
    public async Task CompleteCustomCommandDraft_SavingThenLoading_RoundTripsConfiguration()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var service = CreateService(dbFactory);

        await SaveValidAsync(
            service,
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
                        DeliveryType = CustomAnnouncementDeliveryType.TwitchAnnouncement,
                        AnnouncementColor = BlokeBot
                            .Persistence
                            .Models
                            .TwitchAnnouncementColor
                            .Purple,
                        RetryDelaySeconds = 3,
                        OccurrenceLifetimeSeconds = 45,
                        Schedule = new WeeklyCustomAnnouncementScheduleEditor
                        {
                            Day = DayOfWeek.Friday,
                            Time = new TimeOnly(19, 30),
                        },
                    },
                ],
            }
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
        announcement.DeliveryType.ShouldBe(CustomAnnouncementDeliveryType.TwitchAnnouncement);
        announcement.AnnouncementColor.ShouldBe(
            BlokeBot.Persistence.Models.TwitchAnnouncementColor.Purple
        );
        announcement.LatestDeliveryResult.ShouldBe(CustomAnnouncementLatestDeliveryResult.None);
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
        await SaveValidAsync(service, hostId, ConfigurationWithCommands(("Command", "original")));
        var editor = await service.LoadConfigurationAsync(hostId, CancellationToken.None);
        editor.Commands.Single().Aliases = "updated";
        editor.MessageEntries.Single().Variants.Single().Text = "Updated reply.";

        var beforeSave = await service.LoadConfigurationAsync(hostId, CancellationToken.None);

        beforeSave.Commands.Single().Aliases.ShouldBe("original");
        beforeSave.MessageEntries.Single().Variants.Single().Text.ShouldBe("Reply text.");

        await SaveValidAsync(service, hostId, editor);

        var afterSave = await service.LoadConfigurationAsync(hostId, CancellationToken.None);
        afterSave.Commands.Single().Aliases.ShouldBe("updated");
        afterSave.MessageEntries.Single().Variants.Single().Text.ShouldBe("Updated reply.");
    }

    [Test]
    public void MutableDraft_Validating_ProducesNormalizedCopyIsolatedCommand()
    {
        var draft = ConfigurationWithCommands((" Command ", "!SECOND, first"));
        draft.MessageEntries.Single().Name = " Reply ";
        draft.MessageEntries.Single().Variants.Single().Text = " Reply text. ";

        var command = ValidCommand(draft);
        draft.MessageEntries.Single().Name = "mutated";
        draft.MessageEntries.Single().Variants.Single().Text = "mutated";
        draft.Commands.Single().Name = "mutated";
        draft.Commands.Single().Aliases = "mutated";
        draft.MessageEntries.Clear();

        command.MessageEntries.Single().Name.ShouldBe("Reply");
        command.MessageEntries.Single().Variants.Single().Text.ShouldBe("Reply text.");
        command.Commands.Single().Name.ShouldBe("Command");
        command.Commands.Single().Aliases.ShouldBe(["first", "second"]);
        (command.MessageEntries is List<CustomMessageLibraryEntryValue>).ShouldBeFalse();
        (command.Commands is List<CustomCommandValue>).ShouldBeFalse();
    }

    [Test]
    public async Task CancelledExecution_Saving_DoesNotStartPersistence()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var service = CreateService(dbFactory);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            service
                .SaveConfiguration(
                    1,
                    ValidCommand(ConfigurationWithCommands(("Command", "command")))
                )
                .ExecuteAsync(cancellation.Token)
                .AsTask()
        );

        await using var db = await dbFactory.CreateDbContextAsync();
        (await db.CustomCommands.CountAsync()).ShouldBe(0);
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

        var builtInCollision = await SaveFailureAsync(
            service,
            hostId,
            ConfigurationWithCommands(("Built in", "points"))
        );
        builtInCollision
            .ShouldBeOfType<CustomCommandConfigurationSaveFailure.BuiltInAliasCollision>()
            .Alias.ShouldBe("points");

        var draftCollision = ValidationErrors(
            ConfigurationWithCommands(("First", "hello"), ("Second", "!HELLO"))
        );
        draftCollision.ShouldContain(error => error.Message.Contains("another custom command"));
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
        await SaveValidAsync(service, hostId, draft);
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

        await SaveValidAsync(service, hostId, update);

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
        await SaveValidAsync(service, hostId, loaded);

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
        await SaveValidAsync(service, hostId, draft);
        var update = await service.LoadConfigurationAsync(hostId, CancellationToken.None);
        update.Commands.Clear();
        update.Announcements.Clear();
        update.Counters.Clear();
        update.MessageEntries.Clear();

        await SaveValidAsync(service, hostId, update);

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
        await SaveValidAsync(
            service,
            firstHostId,
            ConfigurationWithCommands(("Command", "command"))
        );
        var stored = await service.LoadConfigurationAsync(firstHostId, CancellationToken.None);

        var missingId = await service.LoadConfigurationAsync(firstHostId, CancellationToken.None);
        missingId.Commands.Single().Id = 999_999;
        var missingError = await SaveFailureAsync(service, firstHostId, missingId);
        missingError.ShouldBeOfType<CustomCommandConfigurationSaveFailure.StaleEntity>();

        var invalidMessage = ConfigurationWithCommands(("Invalid", "invalid"));
        invalidMessage.Commands.Single().Action.MessageLibraryEntryId = -999;
        var messageErrors = ValidationErrors(invalidMessage);
        messageErrors.ShouldContain(error => error.Message.Contains("Choose a saved reply"));

        var invalidCounter = ConfigurationWithCommands(("Counter", "counter"));
        invalidCounter.Commands.Single().Action = new CounterCustomCommandActionEditor
        {
            MessageLibraryEntryId = -1,
            CounterId = -999,
        };
        var counterErrors = ValidationErrors(invalidCounter);
        counterErrors.ShouldContain(error => error.Message.Contains("Choose a counter"));

        var hostBoundaryError = await SaveFailureAsync(service, secondHostId, stored);
        hostBoundaryError.ShouldBeOfType<CustomCommandConfigurationSaveFailure.StaleEntity>();

        var unchanged = await service.LoadConfigurationAsync(firstHostId, CancellationToken.None);
        unchanged.Commands.Single().Name.ShouldBe("Command");
        unchanged.Commands.Single().Aliases.ShouldBe("command");
    }

    [Test]
    public void InvalidAnnouncementSchedule_Validating_ReturnsTypedErrors()
    {
        var interval = ConfigurationWithAnnouncement(
            new IntervalCustomAnnouncementScheduleEditor { IntervalMinutes = 0 }
        );
        ValidationErrors(interval).ShouldContain(error => error.Message.Contains("at least 1"));

        var afterChat = ConfigurationWithAnnouncement(
            new IntervalAfterChatCustomAnnouncementScheduleEditor
            {
                IntervalMinutes = 10,
                RequiredChatMessages = 0,
            }
        );
        ValidationErrors(afterChat)
            .ShouldContain(error => error.Message.Contains("at least 1 chat message"));
    }

    [Test]
    public async Task InvalidAnnouncementDeliveryTiming_Validating_DoesNotPersist()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var missing = ConfigurationWithAnnouncement(new IntervalCustomAnnouncementScheduleEditor());
        missing.Announcements.Single().RetryDelaySeconds = 0;
        ValidationErrors(missing)
            .ShouldContain(error => error.Message.Contains("retry delay must be positive"));

        var excessive = ConfigurationWithAnnouncement(
            new IntervalCustomAnnouncementScheduleEditor()
        );
        excessive.Announcements.Single().OccurrenceLifetimeSeconds = 61;
        ValidationErrors(excessive)
            .ShouldContain(error => error.Message.Contains("no greater than 60"));

        var inconsistent = ConfigurationWithAnnouncement(
            new IntervalCustomAnnouncementScheduleEditor()
        );
        inconsistent.Announcements.Single().RetryDelaySeconds = 30;
        inconsistent.Announcements.Single().OccurrenceLifetimeSeconds = 30;
        ValidationErrors(inconsistent)
            .ShouldContain(error => error.Message.Contains("less than its occurrence lifetime"));

        await using var db = await dbFactory.CreateDbContextAsync();
        (await db.CustomAnnouncements.CountAsync()).ShouldBe(0);
        (await db.CustomAnnouncementDeliveryPolicies.CountAsync()).ShouldBe(0);
    }

    [Test]
    public void NativeAnnouncementReplyOver500Characters_Validating_ReturnsBusinessError()
    {
        var configuration = ConfigurationWithAnnouncement(
            new IntervalCustomAnnouncementScheduleEditor()
        );
        configuration.Announcements.Single().DeliveryType =
            CustomAnnouncementDeliveryType.TwitchAnnouncement;
        configuration.MessageEntries.Single().Variants.Single().Text = new string('x', 501);

        ValidationErrors(configuration)
            .ShouldContain(error => error.Message.Contains("at most 500 characters"));
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
            new AvailableTwitchAnnouncementReadinessProvider(),
            events
        );
    }

    private sealed class AvailableTwitchAnnouncementReadinessProvider
        : ITwitchAnnouncementReadinessProvider
    {
        public Task<TwitchAnnouncementReadiness> GetReadinessAsync(
            string channelLogin,
            CancellationToken cancellationToken
        )
        {
            return Task.FromResult(
                new TwitchAnnouncementReadiness(TwitchAnnouncementAvailability.Available, "bot")
            );
        }
    }

    private static CustomCommandConfigurationSaveCommand ValidCommand(
        CustomCommandConfiguration draft
    )
    {
        return CustomCommandConfigurationValidator
            .Validate(draft)
            .Match(
                command => command,
                errors =>
                    throw new InvalidOperationException(
                        string.Join(" ", errors.Select(error => error.Message))
                    )
            );
    }

    private static IReadOnlyList<CustomCommandConfigurationValidationError> ValidationErrors(
        CustomCommandConfiguration draft
    )
    {
        return CustomCommandConfigurationValidator
            .Validate(draft)
            .Match(_ => Array.Empty<CustomCommandConfigurationValidationError>(), errors => errors);
    }

    private static async Task SaveValidAsync(
        CustomCommandConfigurationService service,
        int hostId,
        CustomCommandConfiguration draft
    )
    {
        var result = await service
            .SaveConfiguration(hostId, ValidCommand(draft))
            .ExecuteAsync(CancellationToken.None);
        result.Match(
            static _ => true,
            failure => throw new InvalidOperationException(failure.Message)
        );
    }

    private static async Task<CustomCommandConfigurationSaveFailure> SaveFailureAsync(
        CustomCommandConfigurationService service,
        int hostId,
        CustomCommandConfiguration draft
    )
    {
        var result = await service
            .SaveConfiguration(hostId, ValidCommand(draft))
            .ExecuteAsync(CancellationToken.None);
        return result.Match(
            _ => throw new InvalidOperationException("Expected custom-command save failure."),
            failure => failure
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
