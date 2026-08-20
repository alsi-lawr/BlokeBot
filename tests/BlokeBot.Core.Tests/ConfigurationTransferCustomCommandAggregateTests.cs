using BlokeBot.Core.Auth.Moderation;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.ConfigurationTransfer;
using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;
using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Core.Hosts;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class ConfigurationTransferCustomCommandAggregateTests
{
    [Test]
    [Arguments(ImportConflictStrategy.AddMissing, 1, 0, 2, 0)]
    [Arguments(ImportConflictStrategy.Merge, 1, 2, 0, 0)]
    [Arguments(ImportConflictStrategy.ReplaceSection, 1, 2, 0, 3)]
    public async Task ReplyOnlyImport_AppliesCompleteStrategyAndReportsChangedSection(
        ImportConflictStrategy strategy,
        int add,
        int update,
        int skip,
        int remove
    )
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedAggregateAsync(database);
        var section = new CustomCommandsSectionV1(
            "Europe/London",
            [
                Reply("shared-reply", "Shared reply", "After"),
                Reply("new-reply", "New reply", "New"),
            ],
            [],
            []
        );
        var selection = Selection(hostId, strategy);
        var document = Document(section);

        var preview = await new ConfigurationImportPreviewService(database).PreviewAsync(
            document,
            selection,
            CancellationToken.None
        );
        preview
            .ShouldBeOfType<ConfigurationPreviewOutcome.Success>()
            .Preview.Sections.Single()
            .Counts.ShouldBe(new(add, update, skip, remove));

        var applied = await Coordinator(database)
            .ApplyAsync(
                Session(hostId),
                document,
                selection,
                new("actor-id", "destination"),
                CancellationToken.None
            );

        applied
            .ShouldBeOfType<ConfigurationImportApplyOutcome.Applied>()
            .Result.ChangedSections.ShouldBe([ConfigurationSectionId.CustomCommands]);
        await using var verify = await database.CreateDbContextAsync();
        var replies = await verify
            .CustomMessageLibraryEntries.Include(x => x.Variants)
            .OrderBy(x => x.Name)
            .ToArrayAsync();
        replies
            .Select(x => x.Name)
            .ShouldBe(
                strategy == ImportConflictStrategy.ReplaceSection
                    ? ["New reply", "Shared reply"]
                    : ["New reply", "Old reply", "Shared reply"]
            );
        replies
            .Single(x => x.Name == "Shared reply")
            .Variants.Single()
            .Text.ShouldBe(strategy == ImportConflictStrategy.AddMissing ? "Before" : "After");
        (await verify.CustomCounters.CountAsync()).ShouldBe(
            strategy == ImportConflictStrategy.ReplaceSection ? 0 : 2
        );
        (await verify.Hosts.SingleAsync()).TimeZoneId.ShouldBe(
            strategy == ImportConflictStrategy.AddMissing ? "UTC" : "Europe/London"
        );
        var audit = await verify.ConfigurationImportAudits.SingleAsync();
        audit.SummaryJson.ShouldBe("{\"Sections\":[{\"Id\":\"customCommands\",\"Count\":3}]}");
        audit.SummaryJson.Length.ShouldBeLessThanOrEqualTo(2048);
    }

    [Test]
    [Arguments(ImportConflictStrategy.AddMissing, 1, 0, 2, 0)]
    [Arguments(ImportConflictStrategy.Merge, 1, 2, 0, 0)]
    [Arguments(ImportConflictStrategy.ReplaceSection, 1, 2, 0, 3)]
    public async Task CounterOnlyImport_AppliesCompleteStrategyAndReportsChangedSection(
        ImportConflictStrategy strategy,
        int add,
        int update,
        int skip,
        int remove
    )
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedAggregateAsync(database);
        var section = new CustomCommandsSectionV1(
            "Europe/London",
            [],
            [new("shared-counter", "Shared counter", 9), new("new-counter", "New counter", 2)],
            []
        );
        var selection = Selection(hostId, strategy);
        var document = Document(section);

        var preview = await new ConfigurationImportPreviewService(database).PreviewAsync(
            document,
            selection,
            CancellationToken.None
        );
        preview
            .ShouldBeOfType<ConfigurationPreviewOutcome.Success>()
            .Preview.Sections.Single()
            .Counts.ShouldBe(new(add, update, skip, remove));

        var applied = await Coordinator(database)
            .ApplyAsync(
                Session(hostId),
                document,
                selection,
                new("actor-id", "destination"),
                CancellationToken.None
            );

        applied
            .ShouldBeOfType<ConfigurationImportApplyOutcome.Applied>()
            .Result.ChangedSections.ShouldBe([ConfigurationSectionId.CustomCommands]);
        await using var verify = await database.CreateDbContextAsync();
        var counters = await verify.CustomCounters.OrderBy(x => x.Name).ToArrayAsync();
        counters
            .Select(x => x.Name)
            .ShouldBe(
                strategy == ImportConflictStrategy.ReplaceSection
                    ? ["New counter", "Shared counter"]
                    : ["New counter", "Old counter", "Shared counter"]
            );
        counters
            .Single(x => x.Name == "Shared counter")
            .Value.ShouldBe(strategy == ImportConflictStrategy.AddMissing ? 1 : 9);
        (await verify.CustomMessageLibraryEntries.CountAsync()).ShouldBe(
            strategy == ImportConflictStrategy.ReplaceSection ? 0 : 2
        );
        (await verify.Hosts.SingleAsync()).TimeZoneId.ShouldBe(
            strategy == ImportConflictStrategy.AddMissing ? "UTC" : "Europe/London"
        );
        var audit = await verify.ConfigurationImportAudits.SingleAsync();
        audit.SummaryJson.ShouldBe("{\"Sections\":[{\"Id\":\"customCommands\",\"Count\":3}]}");
        audit.SummaryJson.Length.ShouldBeLessThanOrEqualTo(2048);
    }

    private static async Task<int> SeedAggregateAsync(SqliteBlokeBotDbFactory database)
    {
        var now = new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc);
        await using var db = await database.CreateDbContextAsync();
        var host = new BotHost
        {
            TwitchUserId = "destination-id",
            Login = "destination",
            DisplayName = "Destination",
            TimeZoneId = "UTC",
            CreatedAtUtc = now,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        db.CustomMessageLibraryEntries.AddRange(
            StoredReply(host.Id, "Shared reply", "Before", now),
            StoredReply(host.Id, "Old reply", "Old", now)
        );
        db.CustomCounters.AddRange(
            new()
            {
                HostId = host.Id,
                Name = "Shared counter",
                Value = 1,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            },
            new()
            {
                HostId = host.Id,
                Name = "Old counter",
                Value = 7,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            }
        );
        _ = await db.SaveChangesAsync();
        return host.Id;
    }

    private static CustomMessageLibraryEntry StoredReply(
        int hostId,
        string name,
        string text,
        DateTime now
    ) =>
        new()
        {
            HostId = hostId,
            Name = name,
            SelectionMode = CustomMessageSelectionMode.Sequential,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            Variants = [new() { SortOrder = 0, Text = text }],
        };

    private static MessageEntryV1 Reply(string id, string name, string text) =>
        new(id, name, CustomMessageSelectionMode.Sequential, [text]);

    private static ConfigurationImportSelection Selection(
        int hostId,
        ImportConflictStrategy strategy
    ) =>
        new(
            hostId,
            [new(ConfigurationSectionId.CustomCommands, strategy, [])],
            new HashSet<HostFeatureFlags>()
        );

    private static ConfigurationDocumentV1 Document(CustomCommandsSectionV1 section) =>
        new(
            ConfigurationDocumentCodec.Format,
            1,
            new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero),
            new("source", "0.12.0"),
            new(CustomCommands: section)
        );

    private static ConfigurationTransferCoordinator Coordinator(SqliteBlokeBotDbFactory database) =>
        new(
            database,
            new(
                new CustomCommandConfigurationGraphWriter(database, null!, TimeProvider.System),
                new(),
                TimeProvider.System
            ),
            new GrantedAuthority(),
            new(),
            TimeProvider.System,
            NullLogger<ConfigurationTransferCoordinator>.Instance
        );

    private static AuthenticatedSession Session(int hostId)
    {
        var host = new BotHostChoice(hostId, "destination", "Destination", AuthRole.Streamer);
        return new()
        {
            IsAuthenticated = true,
            UserId = "destination-id",
            Login = "destination",
            State = new AuthSessionState.Selected(new BotHostSelection(host, [host])),
        };
    }

    private sealed class GrantedAuthority : IModeratorAuthorityService
    {
        public Task<ModeratorAuthorityOutcome> AuthorizeAsync(
            AuthenticatedSession session,
            int requestedHostId,
            CancellationToken ct
        ) => Task.FromResult<ModeratorAuthorityOutcome>(new ModeratorAuthorityOutcome.Granted());
    }
}
