using System.Text;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.ConfigurationTransfer;
using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;
using BlokeBot.Core.Features.ConfigurationTransfer.Page;
using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Core.Hosts;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class ConfigurationTransferStoredValueTests
{
    [Test]
    public async Task Sqlite_LargeDefaultsRoundTripWithoutLiveStateAndOmissionPreservesDestination()
    {
        await using var database = await PointProviderFixture.SqliteFileAsync();
        await RoundTripAsync(database);
    }

    [Test, Explicit]
    public async Task PostgreSql_LargeDefaultsRoundTripWithoutLiveStateAndOmissionPreservesDestination()
    {
        await using var database = await PointProviderFixture.PostgreSqlAsync();
        await RoundTripAsync(database);
    }

    private static async Task RoundTripAsync(IDbContextFactory<BlokeBotDbContext> database)
    {
        var sourceId = await SeedHostAsync(database, "source");
        var destinationId = await SeedHostAsync(database, "destination");
        var values = new CustomStoredValueService(database);
        var largeDefault =
            new string('x', (2 * 1024 * 1024) + 4096) + " exact 😀 {var_inc|global|total|1}";
        (
            await values.SaveDefinitionAsync(
                sourceId,
                new(
                    0,
                    "bio",
                    CustomValueScope.User,
                    CustomValueKind.Text,
                    largeDefault,
                    Guid.Empty
                ),
                default
            )
        ).Status.ShouldBe(CustomValueEditStatus.Saved);
        (
            await values.SaveDefinitionAsync(
                sourceId,
                new(
                    0,
                    "profile",
                    CustomValueScope.User,
                    CustomValueKind.Dictionary,
                    "missing",
                    Guid.Empty
                ),
                default
            )
        ).Status.ShouldBe(CustomValueEditStatus.Saved);
        var definitions = await values.DefinitionsAsync(sourceId, default);
        foreach (var definition in definitions)
        {
            var original = (
                await values.ValueAsync(
                    sourceId,
                    new(
                        definition.Id,
                        "private-viewer",
                        definition.Kind == CustomValueKind.Dictionary ? "private-key" : string.Empty
                    ),
                    default
                )
            )!;
            (
                await values.SaveValueAsync(
                    sourceId,
                    original,
                    CustomValueKind.Text,
                    "private-live-value",
                    default
                )
            ).Status.ShouldBe(CustomValueEditStatus.Saved);
        }
        await using (var seed = await database.CreateDbContextAsync())
        {
            _ = seed.CustomCommandComputedResults.Add(
                new()
                {
                    HostId = sourceId,
                    CommandId = 123,
                    InvocationId = "private-message",
                    InvocationHash = CustomValueIdentity.Hash("private-message"),
                    ViewerId = "private-viewer",
                    Reply = "private-result",
                }
            );
            var reply = new CustomMessageLibraryEntry
            {
                HostId = sourceId,
                Name = "Phrase reply",
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
                Variants = [new() { Text = "{args}" }],
            };
            _ = seed.CustomMessageLibraryEntries.Add(reply);
            _ = await seed.SaveChangesAsync();
            _ = seed.CustomCommands.Add(
                new()
                {
                    HostId = sourceId,
                    Name = "Single phrase",
                    SingleArgument = true,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow,
                    Action = new MessageCustomCommandAction
                    {
                        HostId = sourceId,
                        OneArgumentMessageLibraryEntryId = reply.Id,
                    },
                    Aliases = [new() { HostId = sourceId, Alias = "phrase" }],
                }
            );
            _ = await seed.SaveChangesAsync();
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
        exported.Json.Length.ShouldBeGreaterThan(2 * 1024 * 1024);
        var json = Encoding.UTF8.GetString(exported.Json);
        json.ShouldNotContain("private-");
        var codec = new ConfigurationDocumentCodec();
        var parsedBytes = codec
            .Parse(exported.Json)
            .ShouldBeOfType<ConfigurationDocumentParseOutcome.Valid>()
            .Document;
        var parsedText = codec
            .Parse(json)
            .ShouldBeOfType<ConfigurationDocumentParseOutcome.Valid>()
            .Document;
        parsedBytes
            .Sections.CustomCommands!.StoredDefinitions!.Single(x => x.Name == "bio")
            .Default.ShouldBe(largeDefault);
        parsedText
            .Sections.CustomCommands!.StoredDefinitions!.Single(x => x.Name == "bio")
            .Default.ShouldBe(largeDefault);
        var coordinator = new ConfigurationTransferCoordinator(
            database,
            new(new(database, null!, TimeProvider.System), new(), TimeProvider.System),
            null!,
            new(),
            TimeProvider.System,
            NullLogger<ConfigurationTransferCoordinator>.Instance
        );
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
            [new(ConfigurationSectionId.CustomCommands, ImportConflictStrategy.Merge, [])],
            new HashSet<HostFeatureFlags>()
        );
        _ = (
            await coordinator.ApplyAsync(
                session,
                parsedBytes,
                selection,
                new("destination-id", "destination"),
                default
            )
        ).ShouldBeOfType<ConfigurationImportApplyOutcome.Applied>();
        var imported = await values.DefinitionsAsync(destinationId, default);
        var bio = imported.Single(x => x.Name == "bio");
        (
            await values.ValueAsync(
                destinationId,
                new(bio.Id, "private-viewer", string.Empty),
                default
            )
        )!.Value.ShouldBe(largeDefault);
        (
            await values.ValuesAsync(
                destinationId,
                imported.Single(x => x.Name == "profile").Id,
                "private-viewer",
                default
            )
        ).ShouldBeEmpty();
        var target = (
            await values.ValueAsync(
                destinationId,
                new(bio.Id, "destination-viewer", string.Empty),
                default
            )
        )!;
        (
            await values.SaveValueAsync(
                destinationId,
                target,
                CustomValueKind.Text,
                "destination-kept",
                default
            )
        ).Status.ShouldBe(CustomValueEditStatus.Saved);
        var omission = parsedText with
        {
            Sections = parsedText.Sections with
            {
                CustomCommands = parsedText.Sections.CustomCommands! with
                {
                    StoredDefinitions = null,
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
        (await values.ValueAsync(destinationId, target.Target, default))!.Value.ShouldBe(
            "destination-kept"
        );
        var changed = parsedText with
        {
            Sections = parsedText.Sections with
            {
                CustomCommands = parsedText.Sections.CustomCommands! with
                {
                    StoredDefinitions =
                    [
                        new("bio", CustomValueScope.User, CustomValueKind.Number, "1"),
                    ],
                },
            },
        };
        _ = (
            await coordinator.ApplyAsync(
                session,
                changed,
                selection,
                new("destination-id", "destination"),
                default
            )
        ).ShouldBeOfType<ConfigurationImportApplyOutcome.Invalid>();
        await using var verify = await database.CreateDbContextAsync();
        (
            await verify.CustomCommands.SingleAsync(x => x.HostId == destinationId)
        ).SingleArgument.ShouldBeTrue();
        (
            await verify.CustomStoredValues.Where(x => x.HostId == destinationId).SingleAsync()
        ).Text.ShouldBe("destination-kept");
        (
            await verify
                .CustomCommandComputedResults.Where(x => x.HostId == destinationId)
                .ToListAsync()
        ).ShouldBeEmpty();
    }

    [Test]
    public async Task LargeDefault_UploadAndPasteReachActualImportConsumerIntact()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var uploadId = await SeedHostAsync(database, "upload");
        var pasteId = await SeedHostAsync(database, "paste");
        var largeDefault = new string('y', (2 * 1024 * 1024) + 128) + " exact 😀";
        var document = new ConfigurationDocumentV2(
            ConfigurationDocumentCodec.Format,
            2,
            DateTimeOffset.UtcNow,
            new("source", null),
            new(
                CustomCommands: new(
                    "UTC",
                    [],
                    [],
                    [],
                    [new("bio", CustomValueScope.Global, CustomValueKind.Text, largeDefault)]
                )
            )
        );
        var json = Encoding.UTF8.GetString(new ConfigurationDocumentCodec().Serialize(document));
        foreach (
            var (hostId, login, upload) in new[]
            {
                (uploadId, "upload", true),
                (pasteId, "paste", false),
            }
        )
        {
            await using var context = UiTestContextFactory.Create(database, hostId, login);
            _ = context.Services.AddBlokeBotConfigurationTransfer();
            context
                .Services.GetRequiredService<BunitNavigationManager>()
                .NavigateTo("configuration-transfer#import");
            var page = context.Render<ConfigurationTransferPage>();
            if (upload)
            {
                page.FindComponent<InputFile>()
                    .UploadFiles(InputFileContent.CreateFromText(json, "large.json"));
            }
            else
            {
                page.Find("#configuration-transfer-json").Change(json);
                page.Find("#configuration-transfer-preview").Click();
            }
            page.WaitForAssertion(() =>
                page.Find("#configuration-transfer-apply").HasAttribute("disabled").ShouldBeFalse()
            );
            page.Find("#configuration-transfer-apply").Click();
            page.WaitForAssertion(() =>
            {
                using var verify = database.CreateDbContext();
                verify
                    .CustomValueDefinitions.Single(x => x.HostId == hostId)
                    .DefaultText.ShouldBe(largeDefault);
            });
        }
    }

    private static async Task<int> SeedHostAsync(
        IDbContextFactory<BlokeBotDbContext> database,
        string login
    )
    {
        await using var db = await database.CreateDbContextAsync();
        var host = new BotHost
        {
            Login = login,
            TwitchUserId = $"{login}-id",
            DisplayName = login,
            CreatedAtUtc = DateTime.UtcNow,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        return host.Id;
    }
}
