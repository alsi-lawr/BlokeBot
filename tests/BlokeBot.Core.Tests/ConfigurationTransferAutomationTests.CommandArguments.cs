using System.Text;
using System.Text.Json;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.ConfigurationTransfer;
using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class ConfigurationTransferAutomationTests
{
    [Test]
    public async Task CommandArguments_TransferValidatesFinalCommandsAndFlowsAndRollsBackStructuralBreaks()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var host = await SeedHostAsync(database, "arguments-transfer");
        var source = Node(
            "source",
            "custom-command",
            JsonSerializer.SerializeToElement(
                new Dictionary<string, string> { ["custom-command-id"] = "command-ref" }
            )
        );
        var send = Node(
            "send",
            "send-chat",
            JsonSerializer.SerializeToElement(new { message = "" }),
            [new("message", AutomationInputBindingMode.Connected)]
        );
        var document = AutomationDocument(
            "Typed command",
            [source, send],
            [
                new("flow", AutomationEdgeKind.Flow, "source", "flow", "send", "flow"),
                new("name", AutomationEdgeKind.Data, "source", "argument.name", "send", "message"),
            ]
        );
        document = document with
        {
            Version = 3,
            Sections = document.Sections with
            {
                CustomCommands = new(
                    "UTC",
                    [],
                    [],
                    [
                        new(
                            "command",
                            "Timer",
                            true,
                            ["timer"],
                            true,
                            false,
                            0,
                            CustomCommandCooldownScope.Global,
                            CustomCommandInvocationLimit.Unlimited,
                            new(CustomCommandActionTypeV1.Automation),
                            ArgumentSchema: CustomCommandArgumentTests.TimerSchema
                        ),
                    ]
                ),
                Automations = document.Sections.Automations! with
                {
                    HostReferences =
                    [
                        new("command-ref", AutomationHostReferenceKindV2.CustomCommand, "Timer"),
                    ],
                },
            },
        };
        var selection = new ConfigurationImportSelection(
            host,
            [
                new(ConfigurationSectionId.CustomCommands, ImportConflictStrategy.Merge, []),
                new(ConfigurationSectionId.Automations, ImportConflictStrategy.Merge, []),
            ],
            new HashSet<HostFeatureFlags>()
        );
        var coordinator = Coordinator(
            database,
            new RecordingLogger<ConfigurationTransferCoordinator>()
        );
        _ = (
            await coordinator.ApplyAsync(
                Session(host),
                document,
                await ReviewImportEffectsAsync(database, document, selection),
                new("destination-id", "destination"),
                default
            )
        ).ShouldBeOfType<ConfigurationImportApplyOutcome.Applied>();
        await using (var db = await database.CreateDbContextAsync())
        {
            var command = await db.CustomCommands.SingleAsync();
            command.ArgumentSchema!.Fields[0].ExclusiveMinimum.ShouldBe(0);
            var node = await db.AutomationFlowNodes.SingleAsync(node =>
                node.DefinitionId == "custom-command"
            );
            using var configuration = JsonDocument.Parse(node.ConfigurationJson);
            configuration
                .RootElement.GetProperty("custom-command-id")
                .GetInt32()
                .ShouldBe(command.Id);
        }
        var transfer = AutomationTransfer(database);
        var exported = (
            await new ConfigurationDocumentExporter(
                database,
                new(),
                transfer.Catalog,
                transfer.FlowService,
                TimeProvider.System,
                new AutomationScenarioService(
                    database,
                    transfer.Catalog,
                    transfer.FlowService,
                    TimeProvider.System,
                    new AutomationExpressionService(),
                    BotSettings.FromOptions(new BotOptions()),
                    OAuthAuthorizationScopeSet.Create(["channel:bot"])
                )
            ).ExportAsync(
                host,
                new(
                    new HashSet<ConfigurationSectionId>
                    {
                        ConfigurationSectionId.CustomCommands,
                        ConfigurationSectionId.Automations,
                    },
                    new(false, false, false)
                )
                {
                    AutomationFlowIds = await SelectedFlowIdsAsync(database, host),
                },
                default
            )
        ).ShouldBeOfType<ConfigurationExportOutcome.Success>();
        var exportedRoundtrip = new ConfigurationDocumentCodec()
            .Parse(exported.Json)
            .ShouldBeOfType<ConfigurationDocumentParseOutcome.Valid>()
            .Document;
        exportedRoundtrip.Version.ShouldBe(3);
        exportedRoundtrip
            .Sections.CustomCommands!.Commands.Single()
            .ArgumentSchema!.Fields[0]
            .ExclusiveMinimum.ShouldBe(0);
        exportedRoundtrip
            .Sections.Automations!.Flows.Single()
            .Edges.ShouldContain(edge => edge.SourcePortId == "argument.name");
        // Removing the schema AND its connected field in one import is valid only in the final staged state.
        var noSchema = document with
        {
            Sections = document.Sections with
            {
                CustomCommands = document.Sections.CustomCommands! with
                {
                    Commands =
                    [
                        document.Sections.CustomCommands.Commands[0] with
                        {
                            ArgumentSchema = null,
                        },
                    ],
                },
            },
        };
        var commandOnly = selection with { Sections = [selection.Sections[0]] };
        _ = (
            await coordinator.ApplyAsync(
                Session(host),
                noSchema,
                commandOnly,
                new("destination-id", "destination"),
                default
            )
        ).ShouldBeOfType<ConfigurationImportApplyOutcome.Invalid>();
        await using (var refused = await database.CreateDbContextAsync())
        {
            _ = (await refused.CustomCommands.SingleAsync()).ArgumentSchema.ShouldNotBeNull();
            (await refused.AutomationFlowEdges.CountAsync()).ShouldBe(2);
            (await refused.ConfigurationImportAudits.CountAsync()).ShouldBe(1);
        }
        var flow = noSchema.Sections.Automations!.Flows[0];
        var repair = noSchema with
        {
            Sections = noSchema.Sections with
            {
                Automations = noSchema.Sections.Automations with
                {
                    Flows =
                    [
                        flow with
                        {
                            Nodes =
                            [
                                source,
                                send with
                                {
                                    Configuration = JsonSerializer.SerializeToElement(
                                        new { message = "repaired" }
                                    ),
                                    InputBindings =
                                    [
                                        new("message", AutomationInputBindingMode.Fixed),
                                    ],
                                },
                            ],
                            Edges = [flow.Edges[0]],
                        },
                    ],
                },
            },
        };
        _ = (
            await coordinator.ApplyAsync(
                Session(host),
                repair,
                await ReviewImportEffectsAsync(database, repair, selection),
                new("destination-id", "destination"),
                default
            )
        ).ShouldBeOfType<ConfigurationImportApplyOutcome.Applied>();
        await using var final = await database.CreateDbContextAsync();
        (await final.CustomCommands.SingleAsync()).ArgumentSchema.ShouldBeNull();
        (await final.AutomationFlowEdges.CountAsync()).ShouldBe(1);
        (await final.ConfigurationImportAudits.CountAsync()).ShouldBe(2);
        var codec = new ConfigurationDocumentCodec();
        var encoded = codec.Serialize(document);
        using var wire = JsonDocument.Parse(encoded);
        wire.RootElement.GetProperty("version").GetInt32().ShouldBe(3);
        var roundtrip = codec
            .Parse(encoded)
            .ShouldBeOfType<ConfigurationDocumentParseOutcome.Valid>()
            .Document;
        roundtrip
            .Sections.CustomCommands!.Commands[0]
            .ArgumentSchema!.Fields[0]
            .ExclusiveMinimum.ShouldBe(0);
        var oldVersionWithSchema = Encoding
            .UTF8.GetString(encoded)
            .Replace("\"version\": 3", "\"version\": 2", StringComparison.Ordinal);
        _ = codec
            .Parse(oldVersionWithSchema)
            .ShouldBeOfType<ConfigurationDocumentParseOutcome.Invalid>();
    }
}
