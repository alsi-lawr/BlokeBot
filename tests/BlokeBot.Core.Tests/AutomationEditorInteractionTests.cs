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
        AutomationNodeMoveRequest? moved = null;
        AutomationNodeId? deleted = null;
        var canvas = context.Render<AutomationFlowCanvas>(parameters =>
            parameters
                .Add(component => component.Nodes, [node])
                .Add(component => component.Edges, [])
                .Add(component => component.MoveNode, request => moved = request)
                .Add(component => component.DeleteNode, nodeId => deleted = nodeId)
        );
        var nodeButton = canvas.Find("button[aria-pressed]");

        nodeButton.KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });

        moved.ShouldBe(new AutomationNodeMoveRequest(node.Id, 72, 72));

        nodeButton.KeyDown(new KeyboardEventArgs { Key = "Delete" });

        deleted.ShouldBe(node.Id);
    }
}
