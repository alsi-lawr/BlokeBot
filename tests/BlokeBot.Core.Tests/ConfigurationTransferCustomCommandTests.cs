using System.Text;
using System.Text.Json.Nodes;
using BlokeBot.Core.Features.ConfigurationTransfer;
using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;
using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Persistence.Models;
using BlokeBot.Persistence.Plugins;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class ConfigurationTransferCustomCommandTests
{
    [Test]
    public async Task ExportOmitsViewerIdentityAndMergePreservesDestinationAllowedUsers()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostWithAliasAsync(database, "existing");
        await using (var seed = await database.CreateDbContextAsync())
        {
            var command = await seed.CustomCommands.Include(value => value.Action).SingleAsync();
            var reply = new CustomMessageLibraryEntry
            {
                HostId = hostId,
                Name = "Existing reply",
                SelectionMode = CustomMessageSelectionMode.Sequential,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
                Variants = [new() { SortOrder = 0, Text = "Hello" }],
            };
            _ = seed.CustomMessageLibraryEntries.Add(reply);
            _ = await seed.SaveChangesAsync();
            command.Action.ZeroArgumentMessageLibraryEntryId = reply.Id;
            command.AllowedUsers.Add(
                new()
                {
                    HostId = hostId,
                    TwitchUserId = "viewer-secret-id",
                    Login = "viewer-secret-login",
                    DisplayName = "Viewer Secret Name",
                }
            );
            _ = await seed.SaveChangesAsync();
        }
        var automation = ConfigurationTransferAutomationTestServices.Create(database);
        var exported = (
            await new ConfigurationDocumentExporter(
                database,
                new(),
                automation.Catalog,
                automation.Flows,
                NullLogger<ConfigurationDocumentExporter>.Instance,
                TimeProvider.System,
                new EfPluginFeatureStore(database, new())
            ).ExportAsync(
                hostId,
                new(
                    new HashSet<ConfigurationSectionId> { ConfigurationSectionId.CustomCommands },
                    new(false, false, false)
                ),
                CancellationToken.None
            )
        ).ShouldBeOfType<ConfigurationExportOutcome.Success>();
        var json = Encoding.UTF8.GetString(exported.Json);
        json.ShouldNotContain("allowedUsers");
        json.ShouldNotContain("viewer-secret");

        var withViewerIdentity = JsonNode.Parse(json).ShouldNotBeNull();
        withViewerIdentity["sections"]!["customCommands"]!["commands"]![0]!["allowedUsers"] =
            JsonNode.Parse(
                """[{"twitchUserId":"foreign-id","login":"foreign","displayName":"Foreign Viewer"}]"""
            );
        new ConfigurationDocumentCodec()
            .Parse(withViewerIdentity.ToJsonString())
            .ShouldBeOfType<ConfigurationDocumentParseOutcome.Invalid>()
            .Issue.Location.ShouldContain("allowedUsers");

        var adapter = new CustomCommandConfigurationTransferAdapter(
            new(database, null!, TimeProvider.System),
            new(),
            TimeProvider.System
        );
        await StageAsync(
            database,
            adapter,
            hostId,
            exported.Document,
            new(ConfigurationSectionId.CustomCommands, ImportConflictStrategy.Merge, [])
        );

        await using var verify = await database.CreateDbContextAsync();
        var allowed = await verify.CustomCommandAllowedUsers.SingleAsync();
        allowed.TwitchUserId.ShouldBe("viewer-secret-id");
        allowed.Login.ShouldBe("viewer-secret-login");
        allowed.DisplayName.ShouldBe("Viewer Secret Name");
    }

    [Test]
    [Arguments(ImportConflictStrategy.AddMissing, 2, 0, 1, 0)]
    [Arguments(ImportConflictStrategy.Merge, 2, 1, 0, 0)]
    [Arguments(ImportConflictStrategy.ReplaceSection, 2, 1, 0, 0)]
    public async Task Preview_CustomCommandAggregateCountsMatchSelectedConflictStrategy(
        ImportConflictStrategy strategy,
        int add,
        int update,
        int skip,
        int remove
    )
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostWithAliasAsync(database, "existing");
        var imported = Commands(
            MessageCommand("existing-command", "Existing", "existing"),
            MessageCommand("new-command", "New", "new-command")
        );

        var outcome = await new ConfigurationImportPreviewService(database).PreviewAsync(
            Document(imported),
            new(
                hostId,
                [new(ConfigurationSectionId.CustomCommands, strategy, [])],
                new HashSet<HostFeatureFlags>()
            ),
            CancellationToken.None
        );

        outcome
            .ShouldBeOfType<ConfigurationPreviewOutcome.Success>()
            .Preview.Sections.Single()
            .Counts.ShouldBe(new ConfigurationPreviewCount(add, update, skip, remove));
    }

    [Test]
    public async Task Preview_AutomationActionIsPortableAndAliasConflictsRemainExplicit()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostWithAliasAsync(database, "occupied");
        var section = Commands(
            new(
                "automation",
                "Automation pack",
                true,
                ["automation-pack"],
                true,
                true,
                0,
                CustomCommandCooldownScope.User,
                CustomCommandInvocationLimit.Unlimited,
                new(CustomCommandActionTypeV1.Automation)
            ),
            new(
                "collisions",
                "Collisions",
                true,
                ["request", "occupied"],
                true,
                true,
                0,
                CustomCommandCooldownScope.User,
                CustomCommandInvocationLimit.Unlimited,
                new(CustomCommandActionTypeV1.Message, ZeroArgumentReplyId: "reply")
            )
        );
        var document = Document(section);

        var outcome = await new ConfigurationImportPreviewService(database).PreviewAsync(
            document,
            new(
                hostId,
                [new(ConfigurationSectionId.CustomCommands, ImportConflictStrategy.Merge, [])],
                new HashSet<HostFeatureFlags>()
            ),
            CancellationToken.None
        );

        var conflicts = outcome
            .ShouldBeOfType<ConfigurationPreviewOutcome.Success>()
            .Preview.Sections.Single()
            .Conflicts;
        conflicts.ShouldNotContain(x => x.ImportedId == "automation");
        conflicts.ShouldContain(x => x.ImportedId == "alias:collisions:request");
        conflicts.ShouldContain(x => x.ImportedId == "alias:collisions:occupied");
    }

    [Test]
    public async Task Merge_NormalizesAliasConflictsAndValidatesRenameBeforeApply()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostWithAliasAsync(database, null);
        var section = Commands(
            MessageCommand("incoming", "Incoming", " !MoMeNt "),
            MessageCommand("skipped", "Skipped", "request") with
            {
                Aliases = ["request", "available"],
            }
        );
        var unresolved = new SectionImportSelection(
            ConfigurationSectionId.CustomCommands,
            ImportConflictStrategy.Merge,
            []
        );

        var preview = await new ConfigurationImportPreviewService(database).PreviewAsync(
            Document(section),
            new(hostId, [unresolved], new HashSet<HostFeatureFlags>()),
            CancellationToken.None
        );

        var conflict = preview
            .ShouldBeOfType<ConfigurationPreviewOutcome.Success>()
            .Preview.Sections.Single()
            .Conflicts.Single(candidate =>
                candidate.ImportedId
                == ConfigurationConflictIds.CustomCommandAlias("incoming", " !MoMeNt ")
            );
        conflict.ImportedId.ShouldBe(
            ConfigurationConflictIds.CustomCommandAlias("incoming", " !MoMeNt ")
        );

        var occupiedRename = unresolved with
        {
            ItemResolutions =
            [
                new(
                    conflict.ImportedId,
                    ImportConflictResolution.Rename,
                    ReplacementName: " request "
                ),
            ],
        };
        var occupiedPreview = await new ConfigurationImportPreviewService(database).PreviewAsync(
            Document(section),
            new(hostId, [occupiedRename], new HashSet<HostFeatureFlags>()),
            CancellationToken.None
        );
        occupiedPreview
            .ShouldBeOfType<ConfigurationPreviewOutcome.Success>()
            .Preview.Sections.Single()
            .Issues.ShouldContain(issue => issue.BlocksApply);

        var availableRename = occupiedRename with
        {
            ItemResolutions =
            [
                occupiedRename.ItemResolutions.Single() with
                {
                    ReplacementName = "available",
                },
                new(
                    ConfigurationConflictIds.CustomCommandAlias("skipped", "request"),
                    ImportConflictResolution.Skip
                ),
            ],
        };
        var availablePreview = await new ConfigurationImportPreviewService(database).PreviewAsync(
            Document(section),
            new(hostId, [availableRename], new HashSet<HostFeatureFlags>()),
            CancellationToken.None
        );
        availablePreview
            .ShouldBeOfType<ConfigurationPreviewOutcome.Success>()
            .Preview.Sections.Single()
            .Issues.ShouldNotContain(issue => issue.BlocksApply);
        var adapter = new CustomCommandConfigurationTransferAdapter(
            new(database, null!, TimeProvider.System),
            new(),
            TimeProvider.System
        );
        await StageAsync(database, adapter, hostId, Document(section), availableRename);

        await using var verify = await database.CreateDbContextAsync();
        (await verify.CustomCommandAliases.SingleAsync()).Alias.ShouldBe("available");
    }

    [Test]
    public async Task Merge_UnchangedDestinationFixedAliasCollisionDoesNotBlockNewCommand()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostWithAliasAsync(database, "moment");
        await using (var seed = await database.CreateDbContextAsync())
        {
            var command = await seed.CustomCommands.Include(value => value.Action).SingleAsync();
            var reply = new CustomMessageLibraryEntry
            {
                HostId = hostId,
                Name = "Existing reply",
                SelectionMode = CustomMessageSelectionMode.Sequential,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
                Variants = [new() { SortOrder = 0, Text = "Hello" }],
            };
            _ = seed.CustomMessageLibraryEntries.Add(reply);
            _ = await seed.SaveChangesAsync();
            command.Action.ZeroArgumentMessageLibraryEntryId = reply.Id;
            _ = await seed.SaveChangesAsync();
        }
        var section = Commands(MessageCommand("new-command", "New", "new-command"));
        var adapter = new CustomCommandConfigurationTransferAdapter(
            new(database, null!, TimeProvider.System),
            new(),
            TimeProvider.System
        );

        await StageAsync(
            database,
            adapter,
            hostId,
            Document(section),
            new(ConfigurationSectionId.CustomCommands, ImportConflictStrategy.Merge, [])
        );

        await using var verify = await database.CreateDbContextAsync();
        (await verify.CustomCommands.Select(command => command.Name).ToArrayAsync()).ShouldBe(
            ["Existing", "New"],
            ignoreOrder: true
        );
        (await verify.CustomCommandAliases.Select(alias => alias.Alias).ToArrayAsync()).ShouldBe(
            ["moment", "new-command"],
            ignoreOrder: true
        );
    }

    [Test]
    public async Task Apply_ExplicitWholeCommandSkipDoesNotDowngradeItsAction()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostWithAliasAsync(database, null);
        var section = Commands(
            new(
                "automation",
                "Automation pack",
                true,
                ["automation-pack"],
                true,
                true,
                0,
                CustomCommandCooldownScope.User,
                CustomCommandInvocationLimit.Unlimited,
                new(CustomCommandActionTypeV1.Automation)
            ),
            new(
                "message",
                "Message pack",
                true,
                ["message-pack"],
                true,
                true,
                0,
                CustomCommandCooldownScope.User,
                CustomCommandInvocationLimit.Unlimited,
                new(CustomCommandActionTypeV1.Message, ZeroArgumentReplyId: "reply")
            )
        );
        await using (var db = await database.CreateDbContextAsync())
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            var adapter = new CustomCommandConfigurationTransferAdapter(
                new(database, null!, TimeProvider.System),
                new(),
                TimeProvider.System
            );
            var issues = await adapter.StageAsync(
                db,
                hostId,
                Document(section),
                new(
                    hostId,
                    [
                        new(
                            ConfigurationSectionId.CustomCommands,
                            ImportConflictStrategy.Merge,
                            [new("automation", ImportConflictResolution.Skip)]
                        ),
                    ],
                    new HashSet<HostFeatureFlags>()
                ),
                CancellationToken.None
            );
            issues.ShouldBeEmpty();
            _ = await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        await using var verify = await database.CreateDbContextAsync();
        var command = await verify.CustomCommands.Include(x => x.Action).SingleAsync();
        command.Name.ShouldBe("Message pack");
        _ = command.Action.ShouldBeOfType<MessageCustomCommandAction>();
    }

    [Test]
    public async Task AliasConflictSkip_OmitsTheWholeCommandWithoutCreatingAnInvalidDraft()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostWithAliasAsync(database, null);
        await using (var seed = await database.CreateDbContextAsync())
        {
            seed.CommandAliases.AddRange(
                new()
                {
                    HostId = hostId,
                    Kind = AppCommandKind.Points,
                    Alias = "occupied-one",
                },
                new()
                {
                    HostId = hostId,
                    Kind = AppCommandKind.GivePoints,
                    Alias = "occupied-two",
                }
            );
            _ = await seed.SaveChangesAsync();
        }
        var section = new CustomCommandsSectionV1(
            "UTC",
            [],
            [],
            [
                new(
                    "incoming",
                    "Incoming",
                    true,
                    ["occupied-one", "occupied-two"],
                    true,
                    true,
                    0,
                    CustomCommandCooldownScope.User,
                    CustomCommandInvocationLimit.Unlimited,
                    new(CustomCommandActionTypeV1.Automation)
                ),
            ]
        );
        var selection = new SectionImportSelection(
            ConfigurationSectionId.CustomCommands,
            ImportConflictStrategy.Merge,
            [
                new(
                    ConfigurationConflictIds.CustomCommandAlias("incoming", "occupied-one"),
                    ImportConflictResolution.Skip
                ),
            ]
        );

        var preview = await new ConfigurationImportPreviewService(database).PreviewAsync(
            Document(section),
            new(hostId, [selection], new HashSet<HostFeatureFlags>()),
            CancellationToken.None
        );

        var sectionPreview = preview
            .ShouldBeOfType<ConfigurationPreviewOutcome.Success>()
            .Preview.Sections.Single();
        sectionPreview.Counts.ShouldBe(new ConfigurationPreviewCount(0, 0, 1, 0));
        sectionPreview
            .Conflicts.Single()
            .ImportedId.ShouldBe(
                ConfigurationConflictIds.CustomCommandAlias("incoming", "occupied-one")
            );

        var adapter = new CustomCommandConfigurationTransferAdapter(
            new(database, null!, TimeProvider.System),
            new(),
            TimeProvider.System
        );
        await StageAsync(database, adapter, hostId, Document(section), selection);

        await using var verify = await database.CreateDbContextAsync();
        (await verify.CustomCommands.AnyAsync()).ShouldBeFalse();
        (await verify.CommandAliases.Select(alias => alias.Alias).ToArrayAsync()).ShouldBe(
            ["occupied-one", "occupied-two"],
            ignoreOrder: true
        );
    }

    [Test]
    public async Task CustomOnlyReplace_PreservesSharedAnnouncementReplyAndRemapsCommandRoute()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostWithAliasAsync(database, null);
        var adapter = new CustomCommandConfigurationTransferAdapter(
            new(database, null!, TimeProvider.System),
            new(),
            TimeProvider.System
        );
        var announcements = new AnnouncementsSectionV1(
            [
                new(
                    "announcement-reply",
                    "shared",
                    CustomMessageSelectionMode.Sequential,
                    ["Before"]
                ),
            ],
            [
                new(
                    "announcement",
                    "Reminder",
                    true,
                    "announcement-reply",
                    CustomAnnouncementDeliveryType.ChatMessage,
                    BlokeBot.Persistence.Models.TwitchAnnouncementColor.Primary,
                    2,
                    30,
                    new(AnnouncementScheduleTypeV1.Interval, IntervalMinutes: 60)
                ),
            ]
        );
        await StageAsync(
            database,
            adapter,
            hostId,
            new(
                ConfigurationDocumentCodec.Format,
                1,
                DateTimeOffset.UtcNow,
                new("source", "0.12.0"),
                new(Announcements: announcements)
            ),
            new(ConfigurationSectionId.Announcements, ImportConflictStrategy.Merge, [])
        );
        var commands = new CustomCommandsSectionV1(
            "UTC",
            [new("command-reply", "shared", CustomMessageSelectionMode.Sequential, ["After"])],
            [],
            [
                new(
                    "message",
                    "Message pack",
                    true,
                    ["message-pack"],
                    true,
                    true,
                    0,
                    CustomCommandCooldownScope.User,
                    CustomCommandInvocationLimit.Unlimited,
                    new(CustomCommandActionTypeV1.Message, ZeroArgumentReplyId: "command-reply")
                ),
            ]
        );

        await StageAsync(
            database,
            adapter,
            hostId,
            Document(commands),
            new(ConfigurationSectionId.CustomCommands, ImportConflictStrategy.ReplaceSection, [])
        );

        await using var verify = await database.CreateDbContextAsync();
        var reply = await verify.CustomMessageLibraryEntries.SingleAsync();
        var announcement = await verify.CustomAnnouncements.SingleAsync();
        var command = await verify.CustomCommands.Include(x => x.Action).SingleAsync();
        announcement.MessageLibraryEntryId.ShouldBe(reply.Id);
        command.Action.ZeroArgumentMessageLibraryEntryId.ShouldBe(reply.Id);
        reply.Variants.ShouldBeEmpty();
        (await verify.CustomMessageVariants.SingleAsync()).Text.ShouldBe("After");
    }

    [Test]
    public async Task AnnouncementsOnlyImport_PersistsUtcRecurrenceWithoutChangingDestinationZone()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostWithAliasAsync(database, null);
        await using (var seed = await database.CreateDbContextAsync())
        {
            var host = await seed.Hosts.SingleAsync();
            host.TimeZoneId = "America/Los_Angeles";
            _ = await seed.SaveChangesAsync();
        }
        var adapter = new CustomCommandConfigurationTransferAdapter(
            new(database, null!, TimeProvider.System),
            new(),
            TimeProvider.System
        );
        var announcements = new AnnouncementsSectionV1(
            [new("reply", "Weekly reply", CustomMessageSelectionMode.Sequential, ["Weekly"])],
            [
                new(
                    "weekly",
                    "Weekly",
                    true,
                    "reply",
                    CustomAnnouncementDeliveryType.ChatMessage,
                    BlokeBot.Persistence.Models.TwitchAnnouncementColor.Primary,
                    2,
                    30,
                    new(
                        AnnouncementScheduleTypeV1.Weekly,
                        Day: DayOfWeek.Sunday,
                        Time: new TimeOnly(1, 30)
                    )
                ),
            ]
        );

        await StageAsync(
            database,
            adapter,
            hostId,
            new(
                ConfigurationDocumentCodec.Format,
                1,
                DateTimeOffset.UtcNow,
                new("unlike-source", "0.12.0"),
                new(Announcements: announcements)
            ),
            new(ConfigurationSectionId.Announcements, ImportConflictStrategy.Merge, [])
        );

        await using var verify = await database.CreateDbContextAsync();
        (await verify.Hosts.SingleAsync()).TimeZoneId.ShouldBe("America/Los_Angeles");
        var schedule = await verify
            .CustomAnnouncementSchedules.OfType<WeeklyCustomAnnouncementSchedule>()
            .SingleAsync();
        schedule.Day.ShouldBe(DayOfWeek.Sunday);
        schedule.Time.ShouldBe(new TimeOnly(1, 30));
    }

    private static async Task<int> SeedHostWithAliasAsync(
        SqliteBlokeBotDbFactory database,
        string? alias
    )
    {
        await using var db = await database.CreateDbContextAsync();
        var host = new BotHost
        {
            Login = "destination",
            DisplayName = "Destination",
            CreatedAtUtc = DateTime.UtcNow,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        if (alias is not null)
        {
            _ = db.CustomCommands.Add(
                new()
                {
                    HostId = host.Id,
                    Name = "Existing",
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow,
                    Action = new MessageCustomCommandAction { HostId = host.Id },
                    Aliases = [new() { HostId = host.Id, Alias = alias }],
                }
            );
            _ = await db.SaveChangesAsync();
        }
        return host.Id;
    }

    private static async Task StageAsync(
        SqliteBlokeBotDbFactory database,
        CustomCommandConfigurationTransferAdapter adapter,
        int hostId,
        ConfigurationDocumentV1 document,
        SectionImportSelection section
    )
    {
        await using var db = await database.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var issues = await adapter.StageAsync(
            db,
            hostId,
            document,
            new(hostId, [section], new HashSet<HostFeatureFlags>()),
            CancellationToken.None
        );
        issues.ShouldBeEmpty();
        _ = await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    private static CustomCommandsSectionV1 Commands(params CustomCommandV1[] commands) =>
        new(
            "UTC",
            [new("reply", "reply", CustomMessageSelectionMode.Sequential, ["Hello!"])],
            [],
            commands
        );

    private static CustomCommandV1 MessageCommand(string id, string name, string alias) =>
        new(
            id,
            name,
            true,
            [alias],
            true,
            true,
            0,
            CustomCommandCooldownScope.User,
            CustomCommandInvocationLimit.Unlimited,
            new(CustomCommandActionTypeV1.Message, ZeroArgumentReplyId: "reply")
        );

    private static ConfigurationDocumentV1 Document(CustomCommandsSectionV1 commands) =>
        new(
            ConfigurationDocumentCodec.Format,
            1,
            DateTimeOffset.UtcNow,
            new("source", "0.12.0"),
            new(CustomCommands: commands)
        );
}
