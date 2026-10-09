using System.Text.Json;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.ConfigurationTransfer;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class ConfigurationTransferAutomationTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SendChat_PortableFixedAndConnectedRoundTripWithoutUnusedLiteralValidation(
        bool connected
    )
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "send-transfer");
        var transfer = AutomationTransfer(database);
        var mode = connected
            ? AutomationInputBindingMode.Connected
            : AutomationInputBindingMode.Fixed;
        var configuration = JsonSerializer.SerializeToElement(
            new Dictionary<string, string>
            {
                ["message"] = connected ? string.Empty : "Portable\nfixed message",
            }
        );
        var source = Node(
            "source",
            AutomationDefinitionIds.StreamOnlineSource.Value,
            EmptyObject()
        );
        var send = Node(
            "send",
            AutomationDefinitionIds.SendChatAction.Value,
            configuration,
            [new("message", mode)]
        );
        var transform = Node(
            "transform",
            AutomationDefinitionIds.CelTransform.Value,
            JsonSerializer.Deserialize<JsonElement>(
                """{"inputs":[],"outputs":[{"port-id":"text","display-name":"Text","type":"Text","nullability":"NonNullable","cel":"'connected portable'"}]}"""
            )
        );
        var document = AutomationDocument(
            "Portable send",
            connected ? [source, transform, send] : [source, send],
            connected
                ?
                [
                    new("flow", AutomationEdgeKind.Flow, "source", "flow", "send", "flow"),
                    new("text", AutomationEdgeKind.Data, "transform", "text", "send", "message"),
                ]
                : [new("flow", AutomationEdgeKind.Flow, "source", "flow", "send", "flow")]
        );
        _ = (
            await Coordinator(database, new RecordingLogger<ConfigurationTransferCoordinator>())
                .ApplyAsync(
                    Session(hostId),
                    document,
                    AutomationSelection(hostId),
                    new("destination-id", "destination"),
                    CancellationToken.None
                )
        ).ShouldBeOfType<ConfigurationImportApplyOutcome.Applied>();
        var exported = (
            await ExportAutomationsAsync(database, transfer, hostId)
        ).ShouldBeOfType<ConfigurationExportOutcome.Success>();
        var exportedSend = exported
            .Document.Sections.Automations!.Flows.Single()
            .Nodes.Single(node =>
                node.DefinitionId == AutomationDefinitionIds.SendChatAction.Value
            );
        exportedSend.InputBindings.ShouldBe(send.InputBindings);
        JsonElement.DeepEquals(exportedSend.Configuration, configuration).ShouldBeTrue();
        _ = (
            await Coordinator(database, new RecordingLogger<ConfigurationTransferCoordinator>())
                .ApplyAsync(
                    Session(hostId),
                    exported.Document,
                    AutomationSelection(hostId),
                    new("destination-id", "destination"),
                    CancellationToken.None
                )
        ).ShouldBeOfType<ConfigurationImportApplyOutcome.Applied>();
        await using var verify = await database.CreateDbContextAsync();
        var persisted = await verify.AutomationFlowNodes.SingleAsync(node =>
            node.DefinitionId == AutomationDefinitionIds.SendChatAction.Value
        );
        var bindings = AutomationRuntimeSerialization
            .RestoreInputBindings(persisted.InputBindingsJson)
            .ShouldBeOfType<AutomationInputBindingsRestoreOutcome.Available>()
            .Bindings;
        _ = transfer
            .Catalog.ValidatePersistedDefinition(
                new(
                    persisted.DefinitionId,
                    persisted.DefinitionSchemaVersion,
                    JsonSerializer.Deserialize<JsonElement>(persisted.ConfigurationJson)
                ),
                bindings
            )
            .ShouldBeOfType<AutomationConfigurationCheck.Valid>();
        bindings[new("message")].Mode.ShouldBe(mode);
        (
            await verify.AutomationFlowEdges.CountAsync(edge =>
                edge.Kind == BlokeBot.Persistence.Models.PersistedAutomationEdgeKind.Data
            )
        ).ShouldBe(connected ? 1 : 0);
    }

    [Test]
    public async Task SendChat_UnsupportedPortableExpressionBlocksImportAndStoredExportWithoutMutation()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "unsupported-send-transfer");
        var transfer = AutomationTransfer(database);
        var configuration = JsonSerializer.Deserialize<JsonElement>("""{"message":"retained"}""");
        var document = AutomationDocument(
            "Unsupported send",
            [
                Node("source", AutomationDefinitionIds.StreamOnlineSource.Value, EmptyObject()),
                Node(
                    "send",
                    AutomationDefinitionIds.SendChatAction.Value,
                    configuration,
                    [new("message", AutomationInputBindingMode.Expression, 1, "'unsupported'")]
                ),
            ],
            [new("flow", AutomationEdgeKind.Flow, "source", "flow", "send", "flow")]
        );
        var preview = (
            await new ConfigurationImportPreviewService(
                database,
                UnavailableOverlayConfigurationTransferAdapter.Instance,
                transfer.Adapter
            ).PreviewAsync(document, AutomationSelection(hostId), CancellationToken.None)
        ).ShouldBeOfType<ConfigurationPreviewOutcome.Success>();
        preview.Preview.Sections.Single().Issues.ShouldContain(issue => issue.BlocksApply);
        (
            await Coordinator(database, new RecordingLogger<ConfigurationTransferCoordinator>())
                .ApplyAsync(
                    Session(hostId),
                    document,
                    AutomationSelection(hostId),
                    new("destination-id", "destination"),
                    CancellationToken.None
                )
        )
            .ShouldBeOfType<ConfigurationImportApplyOutcome.Invalid>()
            .Issues.ShouldContain(issue => issue.BlocksApply);
        await using (var verify = await database.CreateDbContextAsync())
        {
            (await verify.AutomationFlows.CountAsync()).ShouldBe(0);
            (await verify.ConfigurationImportAudits.CountAsync()).ShouldBe(0);
        }
        const string Bindings =
            """{"message":{"mode":"Expression","expression":{"languageVersion":1,"source":"'unsupported'"}}}""";
        var stored = await SeedPersistedFlowAsync(
            database,
            hostId,
            "Unsupported stored",
            AutomationDefinitionIds.SendChatAction.Value,
            configuration,
            Bindings
        );
        _ = (
            await ExportAutomationsAsync(database, transfer, hostId)
        ).ShouldBeOfType<ConfigurationExportOutcome.Unsupported>();
        await using var unchanged = await database.CreateDbContextAsync();
        (
            await unchanged.AutomationFlowNodes.SingleAsync(node => node.Id == stored.NodeId)
        ).InputBindingsJson.ShouldBe(Bindings);
    }
}
