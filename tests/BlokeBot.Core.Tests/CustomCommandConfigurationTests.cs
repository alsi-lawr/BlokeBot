using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class CustomCommandConfigurationTests
{
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
                        AllowEveryone = false,
                        AllowModerators = true,
                        AllowedUsers = [new("viewer-id", "viewer", "Viewer")],
                        CooldownSeconds = 12,
                        CooldownScope = CustomCommandCooldownScope.User,
                        InvocationLimit = CustomCommandInvocationLimit.OncePerStreamPerUser,
                        Action = new CounterCustomCommandActionEditor
                        {
                            ReplyRoutes = ReplyRoutes(-1),
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
        entry.Variants.Select(static x => x.Text).ShouldBe(["Hi {user}.", "Hello {channel}."]);

        var counter = loaded.Counters.Single();
        counter.Name.ShouldBe("Deaths");
        counter.Value.ShouldBe(5);

        var command = loaded.Commands.Single();
        command.Name.ShouldBe("Hello");
        command.Aliases.ShouldBe("hi, hello");
        command.Enabled.ShouldBeFalse();
        command.AllowEveryone.ShouldBeFalse();
        command.AllowModerators.ShouldBeTrue();
        command.AllowedUsers.ShouldBe([new("viewer-id", "viewer", "Viewer")]);
        command.CooldownSeconds.ShouldBe(12);
        command.CooldownScope.ShouldBe(CustomCommandCooldownScope.User);
        command.InvocationLimit.ShouldBe(CustomCommandInvocationLimit.OncePerStreamPerUser);
        var action = command.Action.ShouldBeOfType<CounterCustomCommandActionEditor>();
        action.ReplyRoutes.ZeroArgumentMessageLibraryEntryId.ShouldBe(entry.Id);
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
    public async Task OverlayCueCommand_SavingAndLoading_RoundTripsAndRejectsCrossHostOrDisabledReferences()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var otherHostId = await SeedHostAsync(dbFactory, "other");
        var owned = await SeedCueChoicesAsync(dbFactory, hostId, "owned");
        var other = await SeedCueChoicesAsync(dbFactory, otherHostId, "other");
        var references = new RecordingCueAdmissions();
        var service = CreateService(dbFactory, overlayCues: references);
        var draft = ConfigurationWithCommands(("Cue", "cue"));
        draft.Commands.Single().Action = new OverlayCueCustomCommandActionEditor
        {
            TargetOverlayPublicId = owned.TargetId,
            CuePublicId = owned.CueId,
            QueuePolicy = OverlayCueQueuePolicy.Replace,
            ReplyOrder = OverlayCueReplyOrder.Before,
        };

        await SaveValidAsync(service, hostId, draft);
        var loaded = await service.LoadConfigurationAsync(hostId, CancellationToken.None);
        var action = loaded
            .Commands.Single()
            .Action.ShouldBeOfType<OverlayCueCustomCommandActionEditor>();
        action.TargetOverlayPublicId.ShouldBe(owned.TargetId);
        action.CuePublicId.ShouldBe(owned.CueId);
        action.QueuePolicy.ShouldBe(OverlayCueQueuePolicy.Replace);
        action.ReplyOrder.ShouldBe(OverlayCueReplyOrder.Before);
        action.ReplyRoutes.ZeroArgumentMessageLibraryEntryId.ShouldBeNull();

        action.TargetOverlayPublicId = other.TargetId;
        references.Outcome = new OverlayCueReferenceOutcome.Missing(OverlayCueReferencePart.Target);
        _ = (
            await SaveFailureAsync(service, hostId, loaded)
        ).ShouldBeOfType<CustomCommandConfigurationSaveFailure.OverlayCueReference>();
        action.TargetOverlayPublicId = owned.TargetId;
        references.Outcome = new OverlayCueReferenceOutcome.Disabled(OverlayCueReferencePart.Cue);
        _ = (
            await SaveFailureAsync(service, hostId, loaded)
        ).ShouldBeOfType<CustomCommandConfigurationSaveFailure.OverlayCueReference>();
        references.Requests.Count.ShouldBe(3);
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
    public async Task SelectedUsers_CommandAndFeatureDisableCycles_RetainAccessPolicy()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var service = CreateService(dbFactory);
        var draft = ConfigurationWithCommands(("Command", "command"));
        var command = draft.Commands.Single();
        command.AllowEveryone = false;
        command.AllowModerators = true;
        command.AllowedUsers.Add(new("selected-id", "viewer", "Viewer"));
        command.Enabled = false;
        await SaveValidAsync(service, hostId, draft);

        await using (var disabled = await dbFactory.CreateDbContextAsync())
        {
            var host = await disabled.Hosts.SingleAsync(value => value.Id == hostId);
            host.EnabledFeatures &= ~HostFeatureFlags.CustomCommands;
            _ = await disabled.SaveChangesAsync();
            host.EnabledFeatures |= HostFeatureFlags.CustomCommands;
            _ = await disabled.SaveChangesAsync();
        }
        var reloaded = await service.LoadConfigurationAsync(hostId, CancellationToken.None);
        reloaded.Commands.Single().Enabled = true;
        await SaveValidAsync(service, hostId, reloaded);

        var restored = await service.LoadConfigurationAsync(hostId, CancellationToken.None);
        var restoredCommand = restored.Commands.Single();
        restoredCommand.Enabled.ShouldBeTrue();
        restoredCommand.AllowEveryone.ShouldBeFalse();
        restoredCommand.AllowModerators.ShouldBeTrue();
        restoredCommand.AllowedUsers.ShouldBe([new("selected-id", "viewer", "Viewer")]);
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
        command.Commands.Single().Aliases.ShouldBe(["second", "first"]);
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

        _ = await Should.ThrowAsync<OperationCanceledException>(() =>
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
    public async Task BuiltInAndDuplicateDraftAliases_Saving_AllowsShadowAndRejectsCustomCollision()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            _ = db.CommandAliases.Add(
                new CommandAlias
                {
                    HostId = hostId,
                    Kind = AppCommandKind.Points,
                    Alias = "points",
                }
            );
            _ = await db.SaveChangesAsync();
        }

        var service = CreateService(dbFactory);

        await SaveValidAsync(
            service,
            hostId,
            ConfigurationWithCommands(("Built in", "points"), ("Fixed", "request"))
        );
        var loaded = await service.LoadConfigurationAsync(hostId, CancellationToken.None);
        loaded.Commands.Select(static command => command.Aliases).ShouldBe(["points", "request"]);
        loaded.BuiltInAliases.ShouldContain("points");
        loaded.BuiltInAliases.ShouldContain("request");

        var draftCollision = ValidationErrors(
            ConfigurationWithCommands(("First", "hello"), ("Second", "!HELLO"))
        );
        draftCollision.ShouldContain(static error =>
            error.Message.Contains("another custom command")
        );
    }

    [Test]
    public async Task SharedGuessingAlias_SavingCustomCommand_RejectsCollisionWithoutMutation()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var first = new GuessRoundProfile
            {
                HostId = hostId,
                Name = "First",
                Slug = "first",
                IsDefault = true,
            };
            var second = new GuessRoundProfile
            {
                HostId = hostId,
                Name = "Second",
                Slug = "second",
            };
            db.Profiles.AddRange(first, second);
            _ = await db.SaveChangesAsync();
            db.CommandAliases.AddRange(
                new CommandAlias
                {
                    HostId = hostId,
                    GuessRoundProfileId = first.Id,
                    Kind = AppCommandKind.Guess,
                    Alias = "shared-guess",
                },
                new CommandAlias
                {
                    HostId = hostId,
                    GuessRoundProfileId = second.Id,
                    Kind = AppCommandKind.Guess,
                    Alias = "shared-guess",
                }
            );
            _ = await db.SaveChangesAsync();
        }

        var service = CreateService(dbFactory);
        var loaded = await service.LoadConfigurationAsync(hostId, CancellationToken.None);
        var failure = await SaveFailureAsync(
            service,
            hostId,
            ConfigurationWithCommands(("Custom", "shared-guess"))
        );

        loaded.BuiltInAliases.ShouldNotContain("shared-guess");
        failure.ShouldBe(
            new CustomCommandConfigurationSaveFailure.BuiltInAliasCollision("shared-guess")
        );
        await using var verify = await dbFactory.CreateDbContextAsync();
        (await verify.CommandAliases.CountAsync()).ShouldBe(2);
        (await verify.CustomCommands.CountAsync()).ShouldBe(0);
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
        _ = loaded.Commands.Single().Action.ShouldBeOfType<CounterCustomCommandActionEditor>();
        var loadedSchedule = loaded
            .Announcements.Single()
            .Schedule.ShouldBeOfType<IntervalAfterChatCustomAnnouncementScheduleEditor>();
        loadedSchedule.IntervalMinutes.ShouldBe(20);
        loadedSchedule.RequiredChatMessages.ShouldBe(4);
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            (
                await db.CustomCommandAliases.AnyAsync(static x => x.Alias == "old-alias")
            ).ShouldBeFalse();
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
        _ = final.Commands.Single().Action.ShouldBeOfType<MessageCustomCommandActionEditor>();
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
        _ = missingError.ShouldBeOfType<CustomCommandConfigurationSaveFailure.StaleEntity>();

        var invalidMessage = ConfigurationWithCommands(("Invalid", "invalid"));
        invalidMessage.Commands.Single().Action.ReplyRoutes.ZeroArgumentMessageLibraryEntryId =
            -999;
        ValidationErrors(invalidMessage).ShouldNotBeEmpty();

        var invalidCounter = ConfigurationWithCommands(("Counter", "counter"));
        invalidCounter.Commands.Single().Action = new CounterCustomCommandActionEditor
        {
            ReplyRoutes = ReplyRoutes(-1),
            CounterId = -999,
        };
        ValidationErrors(invalidCounter).ShouldNotBeEmpty();

        var invalidAnnouncementSchedule = ConfigurationWithAnnouncement(
            new IntervalCustomAnnouncementScheduleEditor { IntervalMinutes = 0 }
        );
        ValidationErrors(invalidAnnouncementSchedule).ShouldNotBeEmpty();

        var invalidTimeZone = ConfigurationWithCommands();
        invalidTimeZone.TimeZoneId = "Missing/Zone";
        ValidationErrors(invalidTimeZone).ShouldNotBeEmpty();

        var invalidNativeAnnouncement = ConfigurationWithAnnouncement(
            new IntervalCustomAnnouncementScheduleEditor()
        );
        invalidNativeAnnouncement.Announcements.Single().DeliveryType =
            CustomAnnouncementDeliveryType.TwitchAnnouncement;
        invalidNativeAnnouncement.MessageEntries.Single().Variants.Single().Text = new string(
            'x',
            501
        );
        ValidationErrors(invalidNativeAnnouncement).ShouldNotBeEmpty();

        var hostBoundaryError = await SaveFailureAsync(service, secondHostId, stored);
        _ = hostBoundaryError.ShouldBeOfType<CustomCommandConfigurationSaveFailure.StaleEntity>();

        var unchanged = await service.LoadConfigurationAsync(firstHostId, CancellationToken.None);
        unchanged.Commands.Single().Name.ShouldBe("Command");
        unchanged.Commands.Single().Aliases.ShouldBe("command");
    }

    [Test]
    [Arguments("{random_fromage")]
    [Arguments("{random_betweenish")]
    [Arguments("{random_viewer_notes")]
    public void UnknownRandomPrefixCollision_Validating_DoesNotBlockSave(string text)
    {
        var configuration = ConfigurationWithCommands();
        configuration.MessageEntries.Single().Variants.Single().Text = text;

        ValidationErrors(configuration).ShouldBeEmpty();
    }

    [Test]
    public async Task NativeAnnouncementUnavailable_Saving_PersistsDisabled()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(dbFactory, "streamer");
        var service = CreateService(dbFactory, TwitchAnnouncementAvailability.ReconnectRequired);
        var configuration = ConfigurationWithAnnouncement(
            new IntervalCustomAnnouncementScheduleEditor()
        );
        configuration.Announcements.Single().DeliveryType =
            CustomAnnouncementDeliveryType.TwitchAnnouncement;

        await SaveValidAsync(service, hostId, configuration);

        var stored = await service.LoadConfigurationAsync(hostId, CancellationToken.None);
        stored.Announcements.Single().Enabled.ShouldBeFalse();
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
                    Action = new MessageCustomCommandActionEditor { ReplyRoutes = ReplyRoutes(-1) },
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
        SqliteBlokeBotDbFactory dbFactory,
        TwitchAnnouncementAvailability availability = TwitchAnnouncementAvailability.Available,
        IOverlayCueAdmissionService? overlayCues = null
    )
    {
        var events = TestEventBus.Create<AppEventKind>();
        return new CustomCommandConfigurationService(
            dbFactory,
            new CustomCommandAliasRegistry(),
            new CustomCommandConfigurationGraphWriter(
                dbFactory,
                overlayCues ?? new RecordingCueAdmissions(),
                TimeProvider.System
            ),
            new HostCustomCommandSettingsService(dbFactory, events),
            new AvailableTwitchAnnouncementReadinessProvider(availability),
            events,
            TimeProvider.System
        );
    }

    private sealed class RecordingCueAdmissions : IOverlayCueAdmissionService
    {
        public OverlayCueReferenceOutcome Outcome { get; set; } =
            new OverlayCueReferenceOutcome.Available();

        public List<OverlayCueReferenceRequest> Requests { get; } = [];

        public Task<OverlayCueReferenceOutcome> ResolveReferencesAsync(
            OverlayCueReferenceRequest request,
            CancellationToken cancellationToken
        )
        {
            Requests.Add(request);
            return Task.FromResult(Outcome);
        }

        public Task<OverlayCueAdmissionCatalog> QueryCatalogAsync(
            int hostId,
            CancellationToken cancellationToken
        ) => Task.FromResult(new OverlayCueAdmissionCatalog([], []));

        public Task<OverlayCueAdmissionOutcome> AdmitAsync(
            OverlayCueAdmissionRequest request,
            CancellationToken cancellationToken
        ) => Task.FromResult<OverlayCueAdmissionOutcome>(new OverlayCueAdmissionOutcome.Missing());
    }

    private sealed class AvailableTwitchAnnouncementReadinessProvider(
        TwitchAnnouncementAvailability availability
    ) : ITwitchAnnouncementReadinessProvider
    {
        public Task<TwitchAnnouncementReadiness> GetReadinessAsync(
            string channelLogin,
            CancellationToken cancellationToken
        ) => Task.FromResult(new TwitchAnnouncementReadiness(availability, "bot"));
    }

    private static CustomCommandReplyRoutesEditor ReplyRoutes(int? zeroArgumentReplyId) =>
        new() { ZeroArgumentMessageLibraryEntryId = zeroArgumentReplyId };

    private static CustomCommandConfigurationSaveCommand ValidCommand(
        CustomCommandConfiguration draft
    ) =>
        CustomCommandConfigurationValidator
            .Validate(draft)
            .Match(
                command => command,
                errors =>
                    throw new InvalidOperationException(
                        string.Join(" ", errors.Select(error => error.Message))
                    )
            );

    private static IReadOnlyList<CustomCommandConfigurationValidationError> ValidationErrors(
        CustomCommandConfiguration draft
    ) =>
        CustomCommandConfigurationValidator
            .Validate(draft)
            .Match(
                static _ => Array.Empty<CustomCommandConfigurationValidationError>(),
                static errors => errors
            );

    private static async Task SaveValidAsync(
        CustomCommandConfigurationService service,
        int hostId,
        CustomCommandConfiguration draft
    )
    {
        var result = await service
            .SaveConfiguration(hostId, ValidCommand(draft))
            .ExecuteAsync(CancellationToken.None);
        _ = result.Match(
            static _ => true,
            static failure => throw new InvalidOperationException(failure.Message)
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
            static _ =>
                throw new InvalidOperationException("Expected custom-command save failure."),
            static failure => failure
        );
    }

    private static async Task<int> SeedHostAsync(SqliteBlokeBotDbFactory dbFactory, string login)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var host = new BotHost
        {
            EnabledFeatures = HostFeatureFlags.All,
            Login = login,
            DisplayName = login,
            CreatedAtUtc = DateTime.UtcNow,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        return host.Id;
    }

    private static async Task<(Guid TargetId, Guid CueId)> SeedCueChoicesAsync(
        SqliteBlokeBotDbFactory dbFactory,
        int hostId,
        string name
    )
    {
        var targetId = Guid.NewGuid();
        var cueId = Guid.NewGuid();
        await using var db = await dbFactory.CreateDbContextAsync();
        _ = db.OverlayInstances.Add(
            new OverlayInstance
            {
                HostId = hostId,
                PublicId = targetId,
                Name = $"{name} target",
                Type = OverlayType.CuePlayer,
                IsEnabled = true,
                ConfigurationJson = """{"schemaVersion":1}""",
                AccessKeyDigest = System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(name)
                ),
                KeyVersion = 1,
                Revision = 1,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
            }
        );
        _ = db.OverlayCues.Add(
            new OverlayCue
            {
                HostId = hostId,
                PublicId = cueId,
                Name = $"{name} cue",
                IsEnabled = true,
                DurationMilliseconds = 1000,
                QueuePolicy = OverlayCueQueuePolicy.Enqueue,
                ConfigurationJson = """{"schemaVersion":1,"layers":[]}""",
                Revision = 1,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
            }
        );
        _ = await db.SaveChangesAsync();
        return (targetId, cueId);
    }
}
