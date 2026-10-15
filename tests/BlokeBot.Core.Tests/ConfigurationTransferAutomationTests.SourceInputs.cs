using System.Text.Json;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.Automations.Page;
using BlokeBot.Core.Features.ConfigurationTransfer;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class ConfigurationTransferAutomationTests
{
    [Test]
    public async Task SourceInputs_TransferPreservesUnsetFixedDeclarationAndConnectedPortIdentity()
    {
        await using var database = await SqliteBlokeBotDbFactory.CreateAsync();
        var hostId = await SeedHostAsync(database, "source-input-transfer");
        await using (var setup = await database.CreateDbContextAsync())
        {
            (await setup.Hosts.SingleAsync()).EnabledFeatures = BlokeBot
                .Persistence
                .Models
                .HostFeatureFlags
                .Automations;
            _ = await setup.SaveChangesAsync();
        }
        var transfer = AutomationTransfer(database);
        var definitions = (await transfer.Catalog.DiscoverAsync(new(hostId), default)).Definitions;
        var editor = AutomationEditorState.Create("Source-first transfer");
        _ = editor.AddNode(
            definitions.Single(node => node.Id == AutomationDefinitionIds.StreamOnlineSource)
        );
        var definition = definitions.Single(node =>
            node.Id == AutomationDefinitionIds.CelTransform
        );
        var producer = editor.AddNode(definition);
        producer.SetDisplayAlias("Producer");
        var consumer = editor.AddNode(definition);
        consumer.SetDisplayAlias("Consumer");
        var output = producer.Definition.Outputs.Single();
        var input = consumer
            .AddTransformInputFromSource(output, "Source text", "source_text")
            .ShouldNotBeNull();
        var result = consumer.TransformOutputs.Single();
        consumer.UpdateTransformOutput(
            result.PortId,
            result.DisplayName,
            result.ValueType,
            result.Nullability,
            "source_text"
        );
        consumer
            .RenameTransformInput(input.PortId, "renamed_text")
            .ShouldBe(AutomationTransformInputRenameOutcome.Succeeded);
        var renamed = consumer.TransformInputs.Last();
        var edge = new AutomationFlowDraftEdge(
            Guid.NewGuid(),
            AutomationEdgeKind.Data,
            producer.Id,
            output.Id,
            consumer.Id,
            input.PortId
        );
        editor.Edges.Add(edge);
        _ = (
            await transfer.FlowService.SaveAsync(editor.Draft(new(hostId)), default)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        var exported = (
            await ExportAutomationsAsync(database, transfer, hostId)
        ).ShouldBeOfType<ConfigurationExportOutcome.Success>();
        _ = (
            await Coordinator(database, new RecordingLogger<ConfigurationTransferCoordinator>())
                .ApplyAsync(
                    Session(hostId),
                    exported.Document,
                    AutomationSelection(hostId),
                    new("destination-id", "destination"),
                    default
                )
        ).ShouldBeOfType<ConfigurationImportApplyOutcome.Applied>();
        var imported = (await transfer.FlowService.ListAsync(new(hostId), default))
            .ShouldBeOfType<AutomationFlowQueryOutcome.Available>()
            .Flows.ShouldHaveSingleItem();
        var target = imported.Draft.Nodes.Single(node => node.DisplayAlias == "Consumer");
        target.Id.ShouldNotBe(consumer.Id);
        var effective = transfer
            .Catalog.DescribeForAuthoring(new(hostId), target)
            .ShouldBeOfType<AutomationConfigurationCheck.Valid>();
        var restored = AutomationEditorNode.Restore(target, effective.Definition);
        restored.TransformInputs.Last().ShouldBe(renamed);
        restored.TransformOutputs.ShouldBe(consumer.TransformOutputs);
        restored.TransformOutputs.Single().Source.ShouldBe("renamed_text");
        restored
            .TransformInputs.Last()
            .FixedValue.ShouldBe(new AutomationValue.Null(AutomationPortValueType.Text));
        var importedInput = restored.TransformInputs.Last();
        importedInput.PortId.ShouldBe(input.PortId);
        importedInput.BindingFieldId.ShouldBe(input.BindingFieldId);
        target
            .InputBindings[importedInput.BindingFieldId]
            .Mode.ShouldBe(AutomationInputBindingMode.Connected);
        restored
            .Binding(importedInput.BindingFieldId)
            .Mode.ShouldBe(AutomationInputBindingMode.Connected);
        var source = imported.Draft.Nodes.Single(node => node.DisplayAlias == "Producer");
        var importedEdge = imported.Draft.Edges.ShouldHaveSingleItem();
        importedEdge.Id.ShouldNotBe(edge.Id);
        importedEdge.SourceNodeId.ShouldBe(source.Id);
        importedEdge.SourcePortId.ShouldBe(output.Id);
        importedEdge.TargetNodeId.ShouldBe(target.Id);
        importedEdge.TargetPortId.ShouldBe(importedInput.PortId);
        await using var db = await database.CreateDbContextAsync();
        var persisted = await db.AutomationFlowNodes.SingleAsync(node =>
            node.Id == target.Id.Value
        );
        using var document = JsonDocument.Parse(persisted.ConfigurationJson);
        document
            .RootElement.GetProperty("inputs")
            .EnumerateArray()
            .Last()
            .GetProperty("fixed")
            .ValueKind.ShouldBe(JsonValueKind.Null);
    }
}
