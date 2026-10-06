using System.Text.Json.Nodes;
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

public sealed class WatchTimePortableTests
{
    [Test]
    public async Task ActualCodecPreviewApplyExportReimportPreservesSettingsAndOwnGeneration()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await using var fixture = new WatchTimeTestSupport(dbFactory);
        var hostId = await fixture.SeedAsync(amount: "123456789012345678901234567890");
        var original = await ReadAsync(dbFactory);
        var document = await ExportAsync(dbFactory, hostId);
        var codec = new ConfigurationDocumentCodec();
        var parsed = codec
            .Parse(codec.Serialize(document))
            .ShouldBeOfType<ConfigurationDocumentParseOutcome.Valid>()
            .Document;
        var selection = Selection(hostId);
        var preview = (
            await new ConfigurationImportPreviewService(dbFactory).PreviewAsync(
                parsed,
                selection,
                default
            )
        )
            .ShouldBeOfType<ConfigurationPreviewOutcome.Success>()
            .Preview;
        preview.CanApply.ShouldBeTrue();
        _ = (
            await Coordinator(dbFactory, fixture)
                .ApplyAsync(Session(hostId), parsed, selection, new("100", "streamer"), default)
        ).ShouldBeOfType<ConfigurationImportApplyOutcome.Applied>();
        var after = await ReadAsync(dbFactory);
        after.WatchTimeConfigurationRevision.ShouldBe(original.WatchTimeConfigurationRevision);
        after.WatchTimeEnableGeneration.ShouldBe(original.WatchTimeEnableGeneration);
        var exported = await ExportAsync(dbFactory, hostId);
        exported.Sections.Points!.WatchTimePoints.ShouldBe(
            new WatchTimePointsV1(true, original.WatchTimePointAmount)
        );
        var roundTrip = codec
            .Parse(codec.Serialize(exported))
            .ShouldBeOfType<ConfigurationDocumentParseOutcome.Valid>()
            .Document;
        _ = (
            await Coordinator(dbFactory, fixture)
                .ApplyAsync(Session(hostId), roundTrip, selection, new("100", "streamer"), default)
        ).ShouldBeOfType<ConfigurationImportApplyOutcome.Applied>();
        (await ReadAsync(dbFactory)).WatchTimeEnableGeneration.ShouldBe(
            original.WatchTimeEnableGeneration
        );
    }

    [Test]
    public async Task AppliedOldMissingOrNullTurnsOffClearsAmountButSkippedAndUnselectedStayUnchanged()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await using var fixture = new WatchTimeTestSupport(dbFactory);
        var hostId = await fixture.SeedAsync();
        var original = await ReadAsync(dbFactory);
        var document = await ExportAsync(dbFactory, hostId);
        var codec = new ConfigurationDocumentCodec();
        foreach (var missing in new[] { true, false })
        {
            var json = JsonNode.Parse(codec.Serialize(document))!;
            var points = json["sections"]!["points"]!.AsObject();
            if (missing)
            {
                _ = points.Remove("watchTimePoints");
            }
            else
            {
                points["watchTimePoints"] = null;
            }
            var old = codec
                .Parse(json.ToJsonString())
                .ShouldBeOfType<ConfigurationDocumentParseOutcome.Valid>()
                .Document;
            var skipped = Selection(hostId, ImportConflictStrategy.AddMissing);
            _ = (
                await Coordinator(dbFactory, fixture)
                    .ApplyAsync(Session(hostId), old, skipped, new("100", "streamer"), default)
            ).ShouldBeOfType<ConfigurationImportApplyOutcome.Applied>();
            (await ReadAsync(dbFactory)).WatchTimeEnableGeneration.ShouldBe(
                original.WatchTimeEnableGeneration
            );
            var unselected = new ConfigurationImportSelection(
                hostId,
                [],
                new HashSet<HostFeatureFlags>()
            );
            _ = await Coordinator(dbFactory, fixture)
                .ApplyAsync(Session(hostId), old, unselected, new("100", "streamer"), default);
            (await ReadAsync(dbFactory)).WatchTimeEnableGeneration.ShouldBe(
                original.WatchTimeEnableGeneration
            );
            var preview = (
                await new ConfigurationImportPreviewService(dbFactory).PreviewAsync(
                    old,
                    Selection(hostId),
                    default
                )
            )
                .ShouldBeOfType<ConfigurationPreviewOutcome.Success>()
                .Preview;
            preview.CanApply.ShouldBeTrue();
            var issue = preview.Sections.Single().Issues.ShouldHaveSingleItem();
            issue.BlocksApply.ShouldBeFalse();
            _ = (
                await Coordinator(dbFactory, fixture)
                    .ApplyAsync(
                        Session(hostId),
                        old,
                        Selection(hostId),
                        new("100", "streamer"),
                        default
                    )
            ).ShouldBeOfType<ConfigurationImportApplyOutcome.Applied>();
            var off = await ReadAsync(dbFactory);
            off.WatchTimePointsEnabled.ShouldBeFalse();
            off.WatchTimePointAmount.ShouldBeNull();
            off.WatchTimeEnableGeneration.ShouldBe(Guid.Empty);
            _ = await fixture.SaveAsync(hostId, true, "7");
            original = await ReadAsync(dbFactory);
        }
    }

    [Test]
    public async Task StrictNestedMembersAndSemanticInvalidValuesNeverMutate()
    {
        await using var dbFactory = await SqliteBlokeBotDbFactory.CreateAsync();
        await using var fixture = new WatchTimeTestSupport(dbFactory);
        var hostId = await fixture.SeedAsync();
        var original = await ReadAsync(dbFactory);
        var document = await ExportAsync(dbFactory, hostId);
        var codec = new ConfigurationDocumentCodec();
        foreach (var member in new[] { "enabled", "amount" })
        {
            var json = JsonNode.Parse(codec.Serialize(document))!;
            _ = json["sections"]!["points"]!["watchTimePoints"]!.AsObject().Remove(member);
            _ = codec
                .Parse(json.ToJsonString())
                .ShouldBeOfType<ConfigurationDocumentParseOutcome.Invalid>();
        }
        var unknown = JsonNode.Parse(codec.Serialize(document))!;
        unknown["sections"]!["points"]!["watchTimePoints"]!["unexpected"] = 1;
        _ = codec
            .Parse(unknown.ToJsonString())
            .ShouldBeOfType<ConfigurationDocumentParseOutcome.Invalid>();
        var numeric = JsonNode.Parse(codec.Serialize(document))!;
        numeric["sections"]!["points"]!["watchTimePoints"]!["amount"] = 1;
        _ = codec
            .Parse(numeric.ToJsonString())
            .ShouldBeOfType<ConfigurationDocumentParseOutcome.Invalid>();
        foreach (var amount in new string?[] { null, "", "0", "1.5", "-1", new('9', 102) })
        {
            var invalid = document with
            {
                Sections = document.Sections with
                {
                    Points = document.Sections.Points! with { WatchTimePoints = new(true, amount) },
                },
            };
            var parsed = codec
                .Parse(codec.Serialize(invalid))
                .ShouldBeOfType<ConfigurationDocumentParseOutcome.Valid>()
                .Document;
            var preview = (
                await new ConfigurationImportPreviewService(dbFactory).PreviewAsync(
                    parsed,
                    Selection(hostId),
                    default
                )
            )
                .ShouldBeOfType<ConfigurationPreviewOutcome.Success>()
                .Preview;
            preview.CanApply.ShouldBeFalse();
            _ = (
                await Coordinator(dbFactory, fixture)
                    .ApplyAsync(
                        Session(hostId),
                        parsed,
                        Selection(hostId),
                        new("100", "streamer"),
                        default
                    )
            ).ShouldBeOfType<ConfigurationImportApplyOutcome.Invalid>();
            var persisted = await ReadAsync(dbFactory);
            persisted.WatchTimeConfigurationRevision.ShouldBe(
                original.WatchTimeConfigurationRevision
            );
            persisted.WatchTimeEnableGeneration.ShouldBe(original.WatchTimeEnableGeneration);
            persisted.WatchTimePointAmount.ShouldBe(original.WatchTimePointAmount);
        }
        await using var verify = await dbFactory.CreateDbContextAsync();
        (await verify.ConfigurationImportAudits.CountAsync()).ShouldBe(0);
        (await verify.PointLedgerEntries.CountAsync()).ShouldBe(0);
    }

    private static async Task<PointsSettings> ReadAsync(SqliteBlokeBotDbFactory database)
    {
        await using var db = database.CreateDbContext();
        return await db.PointsSettings.AsNoTracking().SingleAsync();
    }

    private static async Task<ConfigurationDocumentV2> ExportAsync(
        SqliteBlokeBotDbFactory database,
        int hostId
    )
    {
        await using var db = database.CreateDbContext();
        return new(
            ConfigurationDocumentCodec.Format,
            2,
            DateTimeOffset.UtcNow,
            new("streamer", "0.17.0"),
            new(Points: await ConfigurationExportMappers.PointsAsync(db, hostId, default))
        );
    }

    private static ConfigurationImportSelection Selection(
        int id,
        ImportConflictStrategy strategy = ImportConflictStrategy.Merge
    ) =>
        new(
            id,
            [new(ConfigurationSectionId.Points, strategy, [])],
            new HashSet<HostFeatureFlags>()
        );

    private static AuthenticatedSession Session(int id)
    {
        var host = new BotHostChoice(id, "streamer", "Streamer", AuthRole.Streamer);
        return new()
        {
            IsAuthenticated = true,
            UserId = "100",
            Login = "streamer",
            State = new AuthSessionState.Selected(new BotHostSelection(host, [host])),
        };
    }

    private static ConfigurationTransferCoordinator Coordinator(
        SqliteBlokeBotDbFactory database,
        WatchTimeTestSupport fixture
    )
    {
        var writer = new CustomCommandConfigurationGraphWriter(database, null!, fixture.Clock);
        return new(
            database,
            new(writer, new(), fixture.Clock),
            new Authority(),
            new(),
            fixture.Clock,
            NullLogger<ConfigurationTransferCoordinator>.Instance,
            fixture.Runtime
        );
    }

    private sealed class Authority : IModeratorAuthorityService
    {
        public Task<ModeratorAuthorityOutcome> AuthorizeAsync(
            AuthenticatedSession session,
            int requestedHostId,
            CancellationToken ct
        ) => Task.FromResult<ModeratorAuthorityOutcome>(new ModeratorAuthorityOutcome.Granted());
    }
}
