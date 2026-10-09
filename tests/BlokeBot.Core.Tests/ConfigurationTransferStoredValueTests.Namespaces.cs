using System.Text;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.ConfigurationTransfer;
using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;
using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Core.Hosts;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class ConfigurationTransferStoredValueTests
{
    [Test]
    public async Task Sqlite_SameNameTransferMatchesNamespacesAndPreservesPrivateDestinationValues()
    {
        await using var database = await PointProviderFixture.SqliteFileAsync();
        await SameNameTransferAsync(database);
    }

    [Test, Explicit]
    public async Task PostgreSql_SameNameTransferMatchesNamespacesAndPreservesPrivateDestinationValues()
    {
        await using var database = await PointProviderFixture.PostgreSqlAsync();
        await SameNameTransferAsync(database);
    }

    private static async Task SameNameTransferAsync(IDbContextFactory<BlokeBotDbContext> database)
    {
        var sourceId = await SeedHostAsync(database, "source");
        var destinationId = await SeedHostAsync(database, "destination");
        await using (var destination = await database.CreateDbContextAsync())
        {
            (await destination.Hosts.SingleAsync(x => x.Id == destinationId)).EnabledFeatures =
                HostFeatureFlags.CustomCommands;
            _ = await destination.SaveChangesAsync();
        }
        var values = new CustomStoredValueService(database);
        var scalar = await CustomCommandExecutionTests.DeclareAsync(
            values,
            sourceId,
            "profile",
            CustomValueScope.User,
            CustomValueKind.Number,
            "2"
        );
        var dictionary = await CustomCommandExecutionTests.DeclareAsync(
            values,
            sourceId,
            "profile",
            CustomValueScope.User,
            CustomValueKind.Dictionary,
            "missing"
        );
        var scalarTarget = (
            await values.ValueAsync(
                sourceId,
                new(scalar.Id, "private-viewer", string.Empty),
                default
            )
        )!;
        (
            await values.SaveValueAsync(
                sourceId,
                scalarTarget,
                CustomValueKind.Number,
                "17",
                default
            )
        ).Status.ShouldBe(CustomValueEditStatus.Saved);
        var entryTarget = (
            await values.ValueAsync(
                sourceId,
                new(dictionary.Id, "private-viewer", "private-key"),
                default
            )
        )!;
        (
            await values.SaveValueAsync(
                sourceId,
                entryTarget,
                CustomValueKind.Text,
                "private-live",
                default
            )
        ).Status.ShouldBe(CustomValueEditStatus.Saved);
        await using (var db = await database.CreateDbContextAsync())
        {
            _ = db.CustomCommandComputedResults.Add(
                new()
                {
                    HostId = sourceId,
                    CommandId = 123,
                    InvocationId = "private-invocation",
                    InvocationHash = CustomValueIdentity.Hash("private-invocation"),
                    ViewerId = "private-viewer",
                    Reply = "private-result",
                }
            );
            _ = await db.SaveChangesAsync();
        }
        var exporter = new ConfigurationDocumentExporter(
            database,
            new(),
            null!,
            null!,
            TimeProvider.System,
            null!
        );
        var exported = (
            await exporter.ExportAsync(
                sourceId,
                new(
                    new HashSet<ConfigurationSectionId> { ConfigurationSectionId.CustomCommands },
                    new(false, false, false)
                ),
                default
            )
        ).ShouldBeOfType<ConfigurationExportOutcome.Success>();
        Encoding.UTF8.GetString(exported.Json).ShouldNotContain("private-");
        var codec = new ConfigurationDocumentCodec();
        var document = codec
            .Parse(exported.Json)
            .ShouldBeOfType<ConfigurationDocumentParseOutcome.Valid>()
            .Document;
        var section = document.Sections.CustomCommands!;
        var definitions = section.StoredDefinitions!;
        definitions.Single(x => x.Kind == CustomValueKind.Number).Default.ShouldBe("2");
        definitions.Single(x => x.Kind == CustomValueKind.Dictionary).Default.ShouldBe("missing");
        foreach (
            var invalid in new IReadOnlyList<StoredDefinitionV1>[]
            {
                [
                    new("profile", CustomValueScope.User, CustomValueKind.Number, "2"),
                    new("profile", CustomValueScope.User, CustomValueKind.Text, "text"),
                ],
                [
                    new("profile", CustomValueScope.User, CustomValueKind.Dictionary, "missing"),
                    new("profile", CustomValueScope.User, CustomValueKind.Dictionary, "other"),
                ],
            }
        )
        {
            var duplicate = document with
            {
                Sections = document.Sections with
                {
                    CustomCommands = section with { StoredDefinitions = invalid },
                },
            };
            _ = codec
                .Parse(codec.Serialize(duplicate))
                .ShouldBeOfType<ConfigurationDocumentParseOutcome.Invalid>();
        }
        var existingScalar = await CustomCommandExecutionTests.DeclareAsync(
            values,
            destinationId,
            "profile",
            CustomValueScope.User,
            CustomValueKind.Number,
            "100"
        );
        var existingTarget = (
            await values.ValueAsync(
                destinationId,
                new(existingScalar.Id, "destination-viewer", string.Empty),
                default
            )
        )!;
        (
            await values.SaveValueAsync(
                destinationId,
                existingTarget,
                CustomValueKind.Number,
                "99",
                default
            )
        ).Status.ShouldBe(CustomValueEditStatus.Saved);
        var host = new BotHostChoice(
            destinationId,
            "destination",
            "destination",
            AuthRole.Streamer
        );
        var session = new AuthenticatedSession
        {
            IsAuthenticated = true,
            UserId = "destination-id",
            Login = "destination",
            State = new AuthSessionState.Selected(new(host, [host])),
        };
        var selection = new ConfigurationImportSelection(
            destinationId,
            [new(ConfigurationSectionId.CustomCommands, ImportConflictStrategy.AddMissing, [])],
            new HashSet<HostFeatureFlags>()
        );
        var previewer = new ConfigurationImportPreviewService(database);
        var preview = (await previewer.PreviewAsync(document, selection, default))
            .ShouldBeOfType<ConfigurationPreviewOutcome.Success>()
            .Preview.Sections.Single();
        preview.Issues.ShouldBeEmpty();
        preview.Counts.ShouldBe(new ConfigurationPreviewCount(1, 0, 1, 0));
        var coordinator = new ConfigurationTransferCoordinator(
            database,
            new(new(database, null!, TimeProvider.System), new(), TimeProvider.System),
            null!,
            new(),
            TimeProvider.System,
            NullLogger<ConfigurationTransferCoordinator>.Instance
        );
        _ = (
            await coordinator.ApplyAsync(
                session,
                document,
                selection,
                new("destination-id", "destination"),
                default
            )
        ).ShouldBeOfType<ConfigurationImportApplyOutcome.Applied>();
        var imported = await values.DefinitionsAsync(destinationId, default);
        imported.Single(x => x.Kind == CustomValueKind.Number).Default.ShouldBe("100");
        var importedDictionary = imported.Single(x => x.Kind == CustomValueKind.Dictionary);
        (
            await values.ValuesAsync(
                destinationId,
                importedDictionary.Id,
                "destination-viewer",
                default
            )
        ).ShouldBeEmpty();
        var destinationEntry = (
            await values.ValueAsync(
                destinationId,
                new(importedDictionary.Id, "destination-viewer", "game"),
                default
            )
        )!;
        (
            await values.SaveValueAsync(
                destinationId,
                destinationEntry,
                CustomValueKind.Text,
                "kept-game",
                default
            )
        ).Status.ShouldBe(CustomValueEditStatus.Saved);
        selection = selection with
        {
            Sections =
            [
                new(ConfigurationSectionId.CustomCommands, ImportConflictStrategy.Merge, []),
            ],
        };
        preview = (await previewer.PreviewAsync(document, selection, default))
            .ShouldBeOfType<ConfigurationPreviewOutcome.Success>()
            .Preview.Sections.Single();
        preview.Issues.ShouldBeEmpty();
        preview.Counts.ShouldBe(new ConfigurationPreviewCount(0, 2, 0, 0));
        _ = (
            await coordinator.ApplyAsync(
                session,
                document,
                selection,
                new("destination-id", "destination"),
                default
            )
        ).ShouldBeOfType<ConfigurationImportApplyOutcome.Applied>();
        (await values.DefinitionsAsync(destinationId, default))
            .Single(x => x.Kind == CustomValueKind.Number)
            .Default.ShouldBe("2");
        var omission = document with
        {
            Sections = document.Sections with
            {
                CustomCommands = section with
                {
                    StoredDefinitions =
                    [
                        definitions.Single(x => x.Kind == CustomValueKind.Dictionary),
                    ],
                },
            },
        };
        _ = (
            await coordinator.ApplyAsync(
                session,
                omission,
                selection with
                {
                    Sections =
                    [
                        new(
                            ConfigurationSectionId.CustomCommands,
                            ImportConflictStrategy.ReplaceSection,
                            []
                        ),
                    ],
                },
                new("destination-id", "destination"),
                default
            )
        ).ShouldBeOfType<ConfigurationImportApplyOutcome.Applied>();
        (await values.ValueAsync(destinationId, existingTarget.Target, default))!.Value.ShouldBe(
            "99"
        );
        (await values.ValueAsync(destinationId, destinationEntry.Target, default))!.Value.ShouldBe(
            "kept-game"
        );
        var typeChange = document with
        {
            Sections = document.Sections with
            {
                CustomCommands = section with
                {
                    StoredDefinitions =
                    [
                        new("profile", CustomValueScope.User, CustomValueKind.Text, "text"),
                    ],
                },
            },
        };
        _ = (
            await coordinator.ApplyAsync(
                session,
                typeChange,
                selection,
                new("destination-id", "destination"),
                default
            )
        ).ShouldBeOfType<ConfigurationImportApplyOutcome.Invalid>();
        (await values.ValueAsync(destinationId, existingTarget.Target, default))!.Value.ShouldBe(
            "99"
        );
        (await values.ValueAsync(destinationId, destinationEntry.Target, default))!.Value.ShouldBe(
            "kept-game"
        );
    }
}
