using BlokeBot.Core.Features.ConfigurationTransfer;
using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;
using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class ConfigurationTransferCustomCommandTests
{
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
    public async Task Preview_ListsBuiltInExistingCustomAndUnsupportedActionConflicts()
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
                [],
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
                [],
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
        conflicts.ShouldContain(x =>
            x.ImportedId == "automation"
            && x.AllowedResolutions.Count == 2
            && x.AllowedResolutions[0] == ImportConflictResolution.Skip
            && x.AllowedResolutions[1] == ImportConflictResolution.Abort
        );
        conflicts.ShouldContain(x => x.ImportedId == "alias:collisions:request");
        conflicts.ShouldContain(x => x.ImportedId == "alias:collisions:occupied");
    }

    [Test]
    public async Task Apply_SkipsWholeUnsupportedCommandWithoutDowngradingItsAction()
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
                [],
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
                [],
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
                    [],
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
            [],
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
