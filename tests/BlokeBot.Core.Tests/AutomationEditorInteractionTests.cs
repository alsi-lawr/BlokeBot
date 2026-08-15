using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.Automations.Page;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class AutomationEditorInteractionTests
{
    [Test]
    public void CanvasKeyboard_MovesOnGridAndRequestsNodeDeletion()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var definition = new CoreAutomationCatalogModule()
            .Definitions.Select(static value => value.Descriptor)
            .Single(static value => value.Id == AutomationDefinitionIds.SendChatAction);
        var firstNode = AutomationEditorNode.Create(definition, new(new(48), new(72)));
        var secondNode = AutomationEditorNode.Create(definition, new(new(96), new(120)));
        IReadOnlyList<AutomationNodeMoveRequest>? moved = null;
        IReadOnlyList<AutomationNodeId>? deleted = null;
        var canvas = context.Render<AutomationFlowCanvas>(parameters =>
            parameters
                .Add(component => component.Nodes, [firstNode, secondNode])
                .Add(component => component.Edges, [])
                .Add(
                    component => component.SelectedNodeIds,
                    new HashSet<AutomationNodeId> { firstNode.Id, secondNode.Id }
                )
                .Add(component => component.MoveNodes, requests => moved = requests)
                .Add(component => component.DeleteNodes, nodeIds => deleted = nodeIds)
        );
        var nodeElement = canvas.Find("[data-automation-node-select]");

        nodeElement.KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });

        moved.ShouldBe([
            new AutomationNodeMoveRequest(firstNode.Id, 72, 72),
            new AutomationNodeMoveRequest(secondNode.Id, 120, 120),
        ]);

        nodeElement.KeyDown(new KeyboardEventArgs { Key = "Delete" });

        deleted.ShouldBe([firstNode.Id, secondNode.Id]);
    }

    [Test]
    public void BindingModes_RoundTripWithoutDiscardingInactiveFixedOrExpressionPayloads()
    {
        var definition = new CoreAutomationCatalogModule()
            .Definitions.Select(static value => value.Descriptor)
            .Single(static value => value.Id == AutomationDefinitionIds.SendChatAction);
        var fieldId = new AutomationConfigurationFieldId("message");
        var expression = new AutomationExpressionSource(
            AutomationExpressionLanguage.CurrentVersion,
            "actor.display_name"
        );
        var node = AutomationEditorNode.Create(definition, new(new(48), new(72)));
        node.SetValue(fieldId, "Retained fixed message");
        node.SetExpression(fieldId, expression);
        node.SetBindingMode(fieldId, AutomationInputBindingMode.Connected);

        var restored = AutomationEditorNode.Restore(node.Draft(), definition);

        restored.Value(fieldId).ShouldBe("Retained fixed message");
        restored.Binding(fieldId).ShouldBe(new(AutomationInputBindingMode.Connected, expression));

        restored.SetBindingMode(fieldId, AutomationInputBindingMode.Fixed);
        restored.Binding(fieldId).Expression.ShouldBe(expression);
        restored.Value(fieldId).ShouldBe("Retained fixed message");

        restored.SetBindingMode(fieldId, AutomationInputBindingMode.Expression);
        var drafted = restored.Draft();
        drafted
            .InputBindings[fieldId]
            .ShouldBe(new(AutomationInputBindingMode.Expression, expression));
        drafted
            .Definition.Configuration.GetProperty("message")
            .GetString()
            .ShouldBe("Retained fixed message");
    }
}
