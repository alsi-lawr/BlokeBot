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
        var node = AutomationEditorNode.Create(definition, new(new(48), new(72)));
        IReadOnlyList<AutomationNodeMoveRequest>? moved = null;
        IReadOnlyList<AutomationNodeId>? deleted = null;
        var canvas = context.Render<AutomationFlowCanvas>(parameters =>
            parameters
                .Add(component => component.Nodes, [node])
                .Add(component => component.Edges, [])
                .Add(
                    component => component.SelectedNodeIds,
                    new HashSet<AutomationNodeId> { node.Id }
                )
                .Add(component => component.MoveNodes, requests => moved = requests)
                .Add(component => component.DeleteNodes, nodeIds => deleted = nodeIds)
        );
        var nodeElement = canvas.Find("[data-automation-node]");

        nodeElement.KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });

        moved.ShouldBe([new AutomationNodeMoveRequest(node.Id, 72, 72)]);

        nodeElement.KeyDown(new KeyboardEventArgs { Key = "Delete" });

        deleted.ShouldBe([node.Id]);
    }
}
