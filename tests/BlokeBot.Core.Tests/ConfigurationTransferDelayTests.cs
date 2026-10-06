using System.Text.Json;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.ConfigurationTransfer;
using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class ConfigurationTransferAutomationTests
{
    [Test]
    [Arguments("legacy-missing")]
    [Arguments("legacy-fixed")]
    [Arguments("legacy-expression")]
    [Arguments("legacy-connected")]
    [Arguments("bound-fixed")]
    [Arguments("bound-expression")]
    [Arguments("bound-connected")]
    public async Task DelayBinding_StrictCodecPreviewApplyExportReimportPreservesSelectedRepresentation(
        string representation
    )
    {
        var mode =
            representation.EndsWith("connected", StringComparison.Ordinal)
                ? AutomationInputBindingMode.Connected
            : representation.EndsWith("expression", StringComparison.Ordinal)
                ? AutomationInputBindingMode.Expression
            : AutomationInputBindingMode.Fixed;
        var bound = representation.StartsWith("bound", StringComparison.Ordinal);
        AutomationInputBindingV2[] bindings =
            representation == "legacy-missing"
                ? []
                :
                [
                    new(
                        bound ? "duration-value-milliseconds" : "duration-milliseconds",
                        mode,
                        mode == AutomationInputBindingMode.Expression ? 1 : null,
                        mode == AutomationInputBindingMode.Expression ? "int(arguments[0])" : null
                    ),
                ];
        var delay = Node(
            "delay",
            "delay",
            JsonSerializer.Deserialize<JsonElement>("""{"duration-milliseconds":17}"""),
            bindings
        );
        var number = Node(
            "number",
            "random-number",
            JsonSerializer.Deserialize<JsonElement>("""{"minimum":23,"maximum":23}""")
        );
        var connected = bound && mode == AutomationInputBindingMode.Connected;
        var source = Node("source", "follow", EmptyObject());
        var document = AutomationDocument(
            "Delay portable",
            connected ? [source, number, delay] : [source, delay],
            connected
                ?
                [
                    new("flow-wire", AutomationEdgeKind.Flow, "source", "flow", "delay", "flow"),
                    new(
                        "duration-wire",
                        AutomationEdgeKind.Data,
                        "number",
                        "number",
                        "delay",
                        "duration"
                    ),
                ]
                : [new("flow-wire", AutomationEdgeKind.Flow, "source", "flow", "delay", "flow")]
        );
        var exported = await DelayTransferCycle(document);
        var reimported = await DelayTransferCycle(exported);
        var flow = reimported.Sections.Automations!.Flows.ShouldHaveSingleItem();
        var restored = flow.Nodes.Single(node => node.DefinitionId == "delay");
        restored.Configuration.GetProperty("duration-milliseconds").GetInt64().ShouldBe(17);
        restored.Configuration.TryGetProperty("duration-value-milliseconds", out _).ShouldBeFalse();
        restored.InputBindings.ShouldBe(bindings);
        if (connected)
        {
            var edge = flow.Edges.Single(edge => edge.Kind == AutomationEdgeKind.Data);
            edge.Kind.ShouldBe(AutomationEdgeKind.Data);
            edge.SourceNodeId.ShouldBe(
                flow.Nodes.Single(node => node.DefinitionId == "random-number").Id
            );
            edge.SourcePortId.ShouldBe("number");
            edge.TargetNodeId.ShouldBe(restored.Id);
            edge.TargetPortId.ShouldBe("duration");
        }
        else
        {
            flow.Edges.ShouldNotContain(edge => edge.Kind == AutomationEdgeKind.Data);
        }
    }

    [Test]
    [Arguments(false, AutomationInputBindingMode.Fixed)]
    [Arguments(false, AutomationInputBindingMode.Connected)]
    [Arguments(true, AutomationInputBindingMode.Fixed)]
    [Arguments(true, AutomationInputBindingMode.Connected)]
    public void DelayBinding_StrictCodecStillRejectsInactiveExpressionPayload(
        bool bound,
        AutomationInputBindingMode mode
    )
    {
        var node = Node(
            "delay",
            "delay",
            JsonSerializer.Deserialize<JsonElement>("""{"duration-milliseconds":17}"""),
            [
                new(
                    bound ? "duration-value-milliseconds" : "duration-milliseconds",
                    mode,
                    1,
                    "int(arguments[0])"
                ),
            ]
        );
        var codec = new ConfigurationDocumentCodec();
        var document = AutomationDocument("Inactive payload", [node], []);
        _ = codec
            .Parse(codec.Serialize(document))
            .ShouldBeOfType<ConfigurationDocumentParseOutcome.Invalid>();
    }

    private static async Task<ConfigurationDocumentV2> DelayTransferCycle(
        ConfigurationDocumentV2 document
    )
    {
        var codec = new ConfigurationDocumentCodec();
        var parsed = codec
            .Parse(codec.Serialize(document))
            .ShouldBeOfType<ConfigurationDocumentParseOutcome.Valid>()
            .Document;
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "delay-transfer");
        var transfer = AutomationTransfer(database);
        var selection = AutomationSelection(hostId);
        var preview = (
            await new ConfigurationImportPreviewService(
                database,
                UnavailableOverlayConfigurationTransferAdapter.Instance,
                transfer.Adapter
            ).PreviewAsync(parsed, selection, CancellationToken.None)
        ).ShouldBeOfType<ConfigurationPreviewOutcome.Success>();
        preview.Preview.Sections.Single().Issues.ShouldAllBe(issue => !issue.BlocksApply);
        _ = (
            await Coordinator(database, new RecordingLogger<ConfigurationTransferCoordinator>())
                .ApplyAsync(
                    Session(hostId),
                    parsed,
                    selection,
                    new("destination-id", "destination"),
                    CancellationToken.None
                )
        ).ShouldBeOfType<ConfigurationImportApplyOutcome.Applied>();
        var exported = (
            await new ConfigurationDocumentExporter(
                database,
                codec,
                transfer.Catalog,
                transfer.FlowService,
                TimeProvider.System,
                new AutomationScenarioService(
                    database,
                    transfer.Catalog,
                    transfer.FlowService,
                    TimeProvider.System
                )
            ).ExportAsync(
                hostId,
                new(
                    new HashSet<ConfigurationSectionId> { ConfigurationSectionId.Automations },
                    new(false, false, false)
                )
                {
                    AutomationFlowIds = await SelectedFlowIdsAsync(database, hostId),
                },
                CancellationToken.None
            )
        ).ShouldBeOfType<ConfigurationExportOutcome.Success>();
        var reserialized = codec
            .Parse(exported.Json)
            .ShouldBeOfType<ConfigurationDocumentParseOutcome.Valid>()
            .Document;
        await using var db = await database.CreateDbContextAsync();
        var persisted = await db.AutomationFlowNodes.SingleAsync(node =>
            node.DefinitionId == "delay"
        );
        var actualBindings = AutomationRuntimeSerialization
            .RestoreInputBindings(persisted.InputBindingsJson)
            .ShouldBeOfType<AutomationInputBindingsRestoreOutcome.Available>()
            .Bindings;
        var expected = parsed
            .Sections.Automations!.Flows.Single()
            .Nodes.Single(node => node.DefinitionId == "delay")
            .InputBindings;
        actualBindings
            .Keys.Select(field => field.Value)
            .Order()
            .ShouldBe(expected.Select(binding => binding.FieldId).Order());
        foreach (var binding in expected)
        {
            var actual = actualBindings[new(binding.FieldId)];
            actual.Mode.ShouldBe(binding.Mode);
            actual.Expression?.Source.ShouldBe(binding.Expression);
        }
        return reserialized;
    }
}
