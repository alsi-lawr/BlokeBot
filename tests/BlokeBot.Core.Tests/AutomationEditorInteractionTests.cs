using System.Collections.Immutable;
using System.Text.Json;
using AngleSharp.Dom;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.Automations.Page;
using BlokeBot.Core.Features.Competitions;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Persistence.Models;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class AutomationEditorInteractionTests
{
    private const string _focusInterop = "Blazor._internal.domWrapper.focus";

    [Test]
    public void TypedEditor_CanvasKeyboard_MovesOnGridAndRequestsNodeDeletion()
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
    public void TypedEditor_CanvasKeyboard_NudgePreservesSingleNodeDisclosure()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var definition = new CoreAutomationCatalogModule()
            .Definitions.Select(static value => value.Descriptor)
            .Single(static value => value.Id == AutomationDefinitionIds.SendChatAction);
        var node = AutomationEditorNode.Create(definition, new(new(48), new(72)));
        IReadOnlyList<AutomationNodeMoveRequest>? moved = null;
        var disclosureClosures = 0;
        var canvas = context.Render<AutomationFlowCanvas>(parameters =>
            parameters
                .Add(component => component.Nodes, [node])
                .Add(component => component.Edges, [])
                .Add(
                    component => component.SelectedNodeIds,
                    new HashSet<AutomationNodeId> { node.Id }
                )
                .Add(component => component.DisclosedNodeId, node.Id)
                .Add(component => component.MoveNodes, requests => moved = requests)
                .Add(component => component.DisclosureClosed, () => disclosureClosures++)
        );

        canvas
            .Find("[data-automation-node-select]")
            .KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });

        moved.ShouldBe([new AutomationNodeMoveRequest(node.Id, 72, 72)]);
        disclosureClosures.ShouldBe(0);
        canvas.Find("[data-automation-node-select]").GetAttribute("aria-expanded").ShouldBe("true");
    }

    [Test]
    public async Task TypedEditor_Canvas_SeparatesSelectionFromSingleNodeDisclosure()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var definitions = new CoreAutomationCatalogModule()
            .Definitions.Select(static value => value.Descriptor)
            .ToArray();
        var firstNode = AutomationEditorNode.Create(
            definitions.Single(static value => value.Id == AutomationDefinitionIds.SendChatAction),
            new(new(48), new(72))
        );
        var secondNode = AutomationEditorNode.Create(
            definitions.Single(static value =>
                value.Id == AutomationDefinitionIds.ConditionControl
            ),
            new(new(288), new(72))
        );
        AutomationNodeId? activated = null;
        AutomationCanvasSelectionRequest? compactSelection = null;
        AutomationCanvasSelectionRequest? pointerSelection = null;
        var canvas = context.Render<AutomationFlowCanvas>(parameters =>
            parameters
                .Add(component => component.Nodes, [firstNode, secondNode])
                .Add(component => component.Edges, [])
                .Add(
                    component => component.SelectedNodeIds,
                    new HashSet<AutomationNodeId> { firstNode.Id, secondNode.Id }
                )
                .Add(component => component.NodeActivated, nodeId => activated = nodeId)
                .Add(component => component.SelectionChanged, value => compactSelection = value)
                .Add(
                    component => component.PointerSelectionChanged,
                    value => pointerSelection = value
                )
        );

        foreach (var button in canvas.FindAll("[data-automation-node-select]"))
        {
            button.GetAttribute("aria-pressed").ShouldBe("true");
            button.GetAttribute("aria-expanded").ShouldBe("false");
        }

        await canvas.Instance.SetPointerSelectionFromCanvasAsync([firstNode.Id.Value]);
        pointerSelection.ShouldNotBeNull().NodeIds.ShouldBe([firstNode.Id]);
        pointerSelection.EdgeId.ShouldBeNull();
        compactSelection.ShouldBeNull();

        await canvas.Instance.ActivateNodeFromCanvasAsync(firstNode.Id.Value);
        activated.ShouldBe(firstNode.Id);

        canvas.Render(parameters =>
            parameters
                .Add(component => component.Nodes, [firstNode, secondNode])
                .Add(component => component.Edges, [])
                .Add(
                    component => component.SelectedNodeIds,
                    new HashSet<AutomationNodeId> { firstNode.Id }
                )
                .Add(component => component.DisclosedNodeId, firstNode.Id)
        );
        var buttons = canvas.FindAll("[data-automation-node-select]");
        buttons[0].GetAttribute("aria-pressed").ShouldBe("true");
        buttons[0].GetAttribute("aria-expanded").ShouldBe("true");
        buttons[1].GetAttribute("aria-pressed").ShouldBe("false");
        buttons[1].GetAttribute("aria-expanded").ShouldBe("false");
    }

    [Test]
    public async Task TypedEditor_Page_ConnectionCreationAndRepairCloseDisclosure()
    {
        await using var fixture = await AutomationEditorPageFixture.CreateAsync();
        var canvas = fixture.Page.FindComponent<AutomationFlowCanvas>();
        var source = canvas.Instance.Nodes.Single(static node =>
            node.Definition.Id == AutomationDefinitionIds.StreamOnlineSource
        );
        var target = canvas.Instance.Nodes.Single(static node =>
            node.Definition.Id == AutomationDefinitionIds.SendChatAction
        );
        var connection = FlowConnection(source, target);
        var originalEdgeId = canvas.Instance.Edges.ShouldHaveSingleItem().Id;

        await fixture.Page.InvokeAsync(() =>
            canvas.Instance.DeleteSelectionFromCanvasAsync([], originalEdgeId)
        );
        fixture.Page.WaitForAssertion(() =>
            fixture.Page.FindComponent<AutomationFlowCanvas>().Instance.Edges.ShouldBeEmpty()
        );
        await DiscloseAsync(fixture.Page, source.Id);

        await fixture.Page.InvokeAsync(() =>
            fixture
                .Page.FindComponent<AutomationFlowCanvas>()
                .Instance.ConnectFromCanvasAsync(
                    connection.SourceNodeId.Value,
                    connection.SourcePortId.Value,
                    connection.TargetNodeId.Value,
                    connection.TargetPortId.Value
                )
        );

        fixture.Page.WaitForAssertion(() =>
        {
            DisclosureCount(fixture.Page).ShouldBe(0);
            _ = fixture
                .Page.FindComponent<AutomationFlowCanvas>()
                .Instance.Edges.ShouldHaveSingleItem();
        });

        canvas = fixture.Page.FindComponent<AutomationFlowCanvas>();
        var createdEdgeId = canvas.Instance.Edges.ShouldHaveSingleItem().Id;
        await DiscloseAsync(fixture.Page, target.Id);
        await fixture.Page.InvokeAsync(() =>
            fixture
                .Page.FindComponent<AutomationNodeInspector>()
                .Instance.Repair.InvokeAsync(new(createdEdgeId, connection))
        );

        fixture.Page.WaitForAssertion(() =>
        {
            DisclosureCount(fixture.Page).ShouldBe(0);
            fixture
                .Page.FindComponent<AutomationFlowCanvas>()
                .Instance.SelectedEdgeId.ShouldBe(createdEdgeId);
        });
    }

    [Test]
    public async Task TypedEditor_Page_DisclosureDoesNotChangeDraftDirtyStateOrHistoryDepth()
    {
        await using var fixture = await AutomationEditorPageFixture.CreateAsync();
        var canvas = fixture.Page.FindComponent<AutomationFlowCanvas>();
        var source = canvas.Instance.Nodes.Single(static node =>
            node.Definition.Id == AutomationDefinitionIds.StreamOnlineSource
        );
        var originalPosition = source.Position;
        var draftBeforeDisclosure = CanvasDraft(canvas.Instance);
        FlowStateButton(fixture.Page).HasAttribute("disabled").ShouldBeFalse();

        await DiscloseAsync(fixture.Page, source.Id);

        CanvasDraft(fixture.Page.FindComponent<AutomationFlowCanvas>().Instance)
            .ShouldBe(draftBeforeDisclosure);
        FlowStateButton(fixture.Page).HasAttribute("disabled").ShouldBeFalse();

        fixture
            .Page.Find(
                $"[data-automation-node='{source.Id.Value:D}'] [data-automation-node-select]"
            )
            .KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        fixture.Page.WaitForAssertion(() =>
        {
            var moved = fixture
                .Page.FindComponent<AutomationFlowCanvas>()
                .Instance.Nodes.Single(node => node.Id == source.Id);
            moved.Position.ShouldNotBe(originalPosition);
            DisclosureCount(fixture.Page).ShouldBe(1);
            FlowStateButton(fixture.Page).HasAttribute("disabled").ShouldBeTrue();
        });

        await fixture.Page.Instance.ApplyEditorHistoryShortcutAsync("undo");
        fixture.Page.WaitForAssertion(() =>
        {
            var restored = fixture
                .Page.FindComponent<AutomationFlowCanvas>()
                .Instance.Nodes.Single(node => node.Id == source.Id);
            restored.Position.ShouldBe(originalPosition);
            DisclosureCount(fixture.Page).ShouldBe(0);
            FlowStateButton(fixture.Page).HasAttribute("disabled").ShouldBeFalse();
        });

        await fixture.Page.Instance.ApplyEditorHistoryShortcutAsync("redo");
        fixture.Page.WaitForAssertion(() =>
        {
            var redone = fixture
                .Page.FindComponent<AutomationFlowCanvas>()
                .Instance.Nodes.Single(node => node.Id == source.Id);
            redone.Position.ShouldNotBe(originalPosition);
            DisclosureCount(fixture.Page).ShouldBe(0);
            FlowStateButton(fixture.Page).HasAttribute("disabled").ShouldBeTrue();
        });
    }

    [Test]
    public async Task TypedEditor_Page_DirectUnavailableProviderFlowSelectionClearsDisclosure()
    {
        await using var fixture = await AutomationEditorPageFixture.CreateAsync(
            includeUnavailableFlow: true
        );
        var source = fixture
            .Page.FindComponent<AutomationFlowCanvas>()
            .Instance.Nodes.Single(static node =>
                node.Definition.Id == AutomationDefinitionIds.StreamOnlineSource
            );
        await DiscloseAsync(fixture.Page, source.Id);

        fixture
            .Page.FindAll(".automation-flow-item")
            .Single(item => item.TextContent.Contains("Unavailable provider flow"))
            .Click();

        fixture.Page.WaitForAssertion(() =>
        {
            fixture.Page.FindAll("[data-automation-canvas]").ShouldBeEmpty();
            DisclosureCount(fixture.Page).ShouldBe(0);
        });
    }

    [Test]
    public async Task TypedEditor_Page_PlainActivationTogglesDisclosureAndMovesItBetweenNodes()
    {
        await using var fixture = await AutomationEditorPageFixture.CreateAsync();
        var canvas = fixture.Page.FindComponent<AutomationFlowCanvas>();
        var source = canvas.Instance.Nodes.Single(static node =>
            node.Definition.Id == AutomationDefinitionIds.StreamOnlineSource
        );
        var target = canvas.Instance.Nodes.Single(static node =>
            node.Definition.Id == AutomationDefinitionIds.SendChatAction
        );

        await DiscloseAsync(fixture.Page, source.Id);
        NodeSelector(fixture.Page, source.Id).GetAttribute("aria-pressed").ShouldBe("true");
        NodeSelector(fixture.Page, source.Id).GetAttribute("aria-expanded").ShouldBe("true");

        await fixture.Page.InvokeAsync(() =>
            fixture
                .Page.FindComponent<AutomationFlowCanvas>()
                .Instance.ActivateNodeFromCanvasAsync(source.Id.Value)
        );
        fixture.Page.WaitForAssertion(() =>
        {
            DisclosureCount(fixture.Page).ShouldBe(0);
            NodeSelector(fixture.Page, source.Id).GetAttribute("aria-pressed").ShouldBe("true");
        });

        await DiscloseAsync(fixture.Page, source.Id);
        await fixture.Page.InvokeAsync(() =>
            fixture
                .Page.FindComponent<AutomationFlowCanvas>()
                .Instance.ActivateNodeFromCanvasAsync(target.Id.Value)
        );
        fixture.Page.WaitForAssertion(() =>
        {
            DisclosureCount(fixture.Page).ShouldBe(1);
            NodeSelector(fixture.Page, target.Id).GetAttribute("aria-expanded").ShouldBe("true");
            NodeSelector(fixture.Page, target.Id).GetAttribute("aria-pressed").ShouldBe("true");
            NodeSelector(fixture.Page, source.Id).GetAttribute("aria-expanded").ShouldBe("false");
            NodeSelector(fixture.Page, source.Id).GetAttribute("aria-pressed").ShouldBe("false");
        });
    }

    [Test]
    public void TypedEditor_BindingModes_RoundTripWithoutDiscardingInactiveFixedOrExpressionPayloads()
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

    [Test]
    public void TypedEditor_ToolboxSearch_RanksAcrossStableCategoriesAndInterleavesAvailability()
    {
        var availableAction = Definition("send-message", AutomationNodeKind.Action, "Send message");
        var unavailableTrigger = Definition(
            "chat-message",
            AutomationNodeKind.Source,
            "Chat message"
        );
        var availableTransform = Definition(
            "message-transform",
            AutomationNodeKind.Transform,
            "Message transform"
        );

        var results = AutomationToolboxCatalog.Query(
            [availableAction, unavailableTrigger, availableTransform],
            AutomationToolboxCategory.Values,
            "message",
            definition =>
                definition.Id == unavailableTrigger.Id
                    ? (false, "BlokeBot needs permission to read chat events.")
                    : (true, "Available in this flow.")
        );

        results
            .Select(static item => item.Definition.Id.Value)
            .ShouldBe(["message-transform", "chat-message", "send-message"]);
        results.Select(static item => item.IsAvailable).ShouldBe([true, false, true]);

        var configuredTransform = availableTransform with
        {
            Display = new("CEL Transform", "Calculate typed values.", "Data"),
            Outputs = [Port("message", AutomationPortValueType.Text)],
        };
        AutomationToolboxCatalog
            .Query(
                [configuredTransform with { Outputs = [] }],
                AutomationToolboxCategory.Values,
                "message",
                static _ => (true, "Available."),
                [configuredTransform]
            )
            .ShouldHaveSingleItem()
            .Definition.Id.ShouldBe(configuredTransform.Id);

        AutomationToolboxCatalog
            .Query(
                [availableAction],
                AutomationToolboxCategory.Values,
                "no result",
                static _ => (true, "Available.")
            )
            .ShouldBeEmpty();
    }

    [Test]
    public void TypedEditor_ToolboxTabs_UseRovingSelectionAndEscapeReturnsClosureControl()
    {
        using var context = new BunitContext();
        var closed = 0;
        var toolbox = context.Render<AutomationToolbox>(parameters =>
            parameters
                .Add(component => component.Definitions, [])
                .Add(component => component.Nodes, [])
                .Add(component => component.Close, () => closed++)
        );
        var tabs = toolbox.FindAll("[role=tab]");

        tabs[1].KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });

        var updatedTabs = toolbox.FindAll("[role=tab]");
        var selectedTab = updatedTabs.Single(tab => tab.GetAttribute("aria-selected") == "true");
        selectedTab.ShouldBe(updatedTabs[2]);
        toolbox.Find("[data-automation-toolbox]").KeyDown(new KeyboardEventArgs { Key = "Escape" });
        closed.ShouldBe(1);
    }

    [Test]
    public void TypedEditor_Canvas_AnnouncesEveryPortWithoutVisualDisclosure()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var conditionDefinition = ProductionDefinitions()
            .Single(definition => definition.Id == AutomationDefinitionIds.ConditionControl);
        var condition = AutomationEditorNode.Create(conditionDefinition, new(new(48), new(72)));
        condition.DisplayAlias =
            "A deliberately long compact node title that remains complete for assistive technology";

        var canvas = context.Render<AutomationFlowCanvas>(parameters =>
            parameters
                .Add(component => component.Nodes, [condition])
                .Add(component => component.Edges, [])
                .Add(component => component.ViewportKey, "accessible-ports")
        );

        var accessibleName = canvas
            .Find("[data-automation-node-select]")
            .GetAttribute("aria-label")
            .ShouldNotBeNull();

        accessibleName.ShouldContain(condition.DisplayAlias);

        foreach (var input in conditionDefinition.Inputs)
        {
            accessibleName.ShouldContain($"{AccessiblePortName(input)} input");
        }
        foreach (var output in conditionDefinition.Outputs)
        {
            accessibleName.ShouldContain($"{AccessiblePortName(output)} output");
        }
    }

    [Test]
    public void TypedEditor_EditorState_ReusesTheFirstFreeGridPosition()
    {
        var definition = Definition("value", AutomationNodeKind.Value, "Value");
        var editor = AutomationEditorState.Create("Flow");
        var first = editor.AddNode(definition);
        var second = editor.AddNode(definition);
        var third = editor.AddNode(definition);
        var removedPosition = second.Position;
        editor.RemoveNode(second.Id);

        var replacement = editor.AddNode(definition);

        replacement.Position.ShouldBe(removedPosition);
        replacement.Position.ShouldNotBe(first.Position);
        replacement.Position.ShouldNotBe(third.Position);
    }

    [Test]
    public void TypedEditor_TypedConnections_AllowFanOutAndRetainAnExactInvalidEdgeForRepair()
    {
        var sourceDefinition = Definition(
            "number-source",
            AutomationNodeKind.Value,
            "Number source",
            outputs: [Port("number", AutomationPortValueType.Number)]
        );
        var targetDefinition = Definition(
            "boolean-target",
            AutomationNodeKind.Control,
            "Boolean target",
            inputs:
            [
                Port(
                    "predicate",
                    AutomationPortValueType.Boolean,
                    bindingFieldId: new("predicate")
                ),
            ]
        );
        var source = AutomationEditorNode.Create(sourceDefinition, new(new(48), new(72)));
        var firstTarget = AutomationEditorNode.Create(targetDefinition, new(new(288), new(72)));
        var secondTarget = AutomationEditorNode.Create(targetDefinition, new(new(288), new(240)));
        var edges = new[]
        {
            new AutomationFlowDraftEdge(
                Guid.NewGuid(),
                AutomationEdgeKind.Data,
                source.Id,
                new("number"),
                firstTarget.Id,
                new("predicate")
            ),
            new AutomationFlowDraftEdge(
                Guid.NewGuid(),
                AutomationEdgeKind.Data,
                source.Id,
                new("number"),
                secondTarget.Id,
                new("predicate")
            ),
        };

        edges.ShouldAllBe(edge => edge.SourceNodeId == source.Id);
        edges.Select(static edge => edge.TargetNodeId).Distinct().Count().ShouldBe(2);
        var issues = edges
            .Select(edge => AutomationConnections.Issue(edge, [source, firstTarget, secondTarget]))
            .ToArray();
        issues.ShouldAllBe(static issue => !string.IsNullOrWhiteSpace(issue));
    }

    [Test]
    public void TypedEditor_ValidationPresentation_ShowsOneRetainedRepairWithoutReachabilityCascades()
    {
        var flowInput = Port("flow", AutomationPortValueType.Flow);
        var flowOutput = Port("flow", AutomationPortValueType.Flow);
        var source = AutomationEditorNode.Create(
            Definition(
                "source",
                AutomationNodeKind.Source,
                "Source",
                outputs: [flowOutput, Port("actor", AutomationPortValueType.Actor)]
            ),
            new(new(24), new(24))
        );
        var number = AutomationEditorNode.Create(
            Definition(
                "number",
                AutomationNodeKind.Value,
                "Number",
                outputs: [Port("number", AutomationPortValueType.Number)]
            ),
            new(new(24), new(192))
        );
        var transform = AutomationEditorNode.Create(
            Definition(
                "transform",
                AutomationNodeKind.Transform,
                "Transform",
                inputs:
                [
                    flowInput,
                    Port("predicate", AutomationPortValueType.Boolean),
                    Port("actor", AutomationPortValueType.Actor),
                ],
                outputs: [flowOutput, Port("message", AutomationPortValueType.Text)]
            ),
            new(new(192), new(24))
        );
        var action = AutomationEditorNode.Create(
            Definition(
                "action",
                AutomationNodeKind.Action,
                "Action",
                inputs: [flowInput, Port("message", AutomationPortValueType.Text)]
            ),
            new(new(360), new(24))
        );
        var invalidEdge = new AutomationFlowDraftEdge(
            Guid.NewGuid(),
            AutomationEdgeKind.Data,
            number.Id,
            new("number"),
            transform.Id,
            new("predicate")
        );
        var edges = new[]
        {
            new AutomationFlowDraftEdge(
                Guid.NewGuid(),
                AutomationEdgeKind.Flow,
                source.Id,
                new("flow"),
                transform.Id,
                new("flow")
            ),
            new AutomationFlowDraftEdge(
                Guid.NewGuid(),
                AutomationEdgeKind.Flow,
                transform.Id,
                new("flow"),
                action.Id,
                new("flow")
            ),
            new AutomationFlowDraftEdge(
                Guid.NewGuid(),
                AutomationEdgeKind.Data,
                source.Id,
                new("actor"),
                transform.Id,
                new("actor")
            ),
            new AutomationFlowDraftEdge(
                Guid.NewGuid(),
                AutomationEdgeKind.Data,
                transform.Id,
                new("message"),
                action.Id,
                new("message")
            ),
            invalidEdge,
        };
        var errors = new[]
        {
            new AutomationGraphError(
                transform.Id,
                "data-type-incompatible",
                "Connect Data ports that have the same exact type.",
                PortId: new("predicate")
            ),
            new AutomationGraphError(
                transform.Id,
                "node-disconnected",
                "Connect this node to a trigger."
            ),
            new AutomationGraphError(
                action.Id,
                "node-disconnected",
                "Connect this node to a trigger."
            ),
            new AutomationGraphError(
                action.Id,
                "data-source-unavailable",
                "This source Data is not available on every Flow path to the input."
            ),
        };

        var presentation = AutomationValidationPresentation.Create(
            errors,
            [source, number, transform, action],
            edges
        );

        presentation.IssueCount.ShouldBe(1);
        presentation.VisibleErrors.ShouldHaveSingleItem().Code.ShouldBe("data-type-incompatible");
        errors.Length.ShouldBe(4);
    }

    [Test]
    public void TypedEditor_Compatibility_UsesTypeNullabilitySensitivityAndOneWaySafetyRules()
    {
        var safeNullableNumber = Port(
            "number",
            AutomationPortValueType.Number,
            nullability: AutomationPortNullability.Nullable
        );
        var safeRequiredNumber = Port("number", AutomationPortValueType.Number);
        var sensitiveNumber = safeRequiredNumber with
        {
            Sensitivity = AutomationDataSensitivity.Sensitive,
        };

        var nullableRejection = AutomationConnections.Compatibility(
            AutomationNodeKind.Value,
            safeNullableNumber,
            safeRequiredNumber
        );
        nullableRejection.IsCompatible.ShouldBeFalse();
        nullableRejection.Reason.ShouldNotBeNullOrWhiteSpace();
        var sensitiveRejection = AutomationConnections.Compatibility(
            AutomationNodeKind.Value,
            sensitiveNumber,
            safeRequiredNumber
        );
        sensitiveRejection.IsCompatible.ShouldBeFalse();
        sensitiveRejection.Reason.ShouldNotBeNullOrWhiteSpace();
        AutomationConnections
            .Compatibility(AutomationNodeKind.Value, safeRequiredNumber, sensitiveNumber)
            .IsCompatible.ShouldBeTrue();
        var sourceKindRejection = AutomationConnections.Compatibility(
            AutomationNodeKind.Action,
            safeRequiredNumber,
            safeRequiredNumber
        );
        sourceKindRejection.IsCompatible.ShouldBeFalse();
        sourceKindRejection.Reason.ShouldNotBeNullOrWhiteSpace();
    }

    [Test]
    public void TypedEditor_SourcePicker_CompatibleSelectionEmitsExactConnectionAndReturnsFocus()
    {
        var nodes = CreatePickerNodes();
        var expected = new AutomationConnectionRequest(
            nodes.CompatibleSource.Id,
            nodes.CompatibleSource.Definition.Outputs.Single().Id,
            nodes.Target.Id,
            nodes.Target.Definition.Inputs.Single().Id
        );
        var completion = ActivatePickerAction(
            nodes.Target,
            [nodes.IncompatibleSource, nodes.CompatibleSource, nodes.Target],
            [],
            "Connect"
        );

        completion.Connection.ShouldBe(expected);
        completion.Repair.ShouldBeNull();
    }

    [Test]
    public void TypedEditor_SourcePicker_RetainedEdgeRepairEmitsExactReplacementAndReturnsFocus()
    {
        var nodes = CreatePickerNodes();
        var incompatibleOutput = nodes.IncompatibleSource.Definition.Outputs.Single();
        var targetInput = nodes.Target.Definition.Inputs.Single();
        var retained = new AutomationFlowDraftEdge(
            Guid.NewGuid(),
            AutomationEdgeKind.Data,
            nodes.IncompatibleSource.Id,
            incompatibleOutput.Id,
            nodes.Target.Id,
            targetInput.Id
        );
        var expected = new AutomationConnectionRequest(
            nodes.CompatibleSource.Id,
            nodes.CompatibleSource.Definition.Outputs.Single().Id,
            nodes.Target.Id,
            targetInput.Id
        );
        var completion = ActivatePickerAction(
            nodes.Target,
            [nodes.IncompatibleSource, nodes.CompatibleSource, nodes.Target],
            [retained],
            "Connect"
        );

        completion.Connection.ShouldBeNull();
        completion.Repair.ShouldBe(new(retained.Id, expected));
        retained.SourceNodeId.ShouldBe(nodes.IncompatibleSource.Id);
        retained.SourcePortId.ShouldBe(incompatibleOutput.Id);
        retained.TargetNodeId.ShouldBe(nodes.Target.Id);
        retained.TargetPortId.ShouldBe(targetInput.Id);
        AutomationConnections
            .Issue(retained, [nodes.IncompatibleSource, nodes.CompatibleSource, nodes.Target])
            .ShouldNotBeNullOrWhiteSpace();
    }

    [Test]
    public void TypedEditor_SourcePicker_IncompatibleChoiceExplainsAndCannotComplete()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var nodes = CreatePickerNodes();
        var output = nodes.IncompatibleSource.Definition.Outputs.Single();
        var input = nodes.Target.Definition.Inputs.Single();
        var compatibility = AutomationConnections.Compatibility(
            nodes.IncompatibleSource,
            output,
            nodes.Target,
            input
        );
        AutomationConnectionRequest? connection = null;
        AutomationRepairConnectionRequest? repair = null;
        compatibility.IsCompatible.ShouldBeFalse();
        compatibility.Reason.ShouldNotBeNullOrWhiteSpace();
        var inspector = context.Render<AutomationNodeInspector>(parameters =>
            parameters
                .Add(component => component.Node, nodes.Target)
                .Add(component => component.Nodes, [nodes.IncompatibleSource, nodes.Target])
                .Add(component => component.Edges, [])
                .Add(component => component.Connect, request => connection = request)
                .Add(component => component.Repair, request => repair = request)
        );

        inspector.Find("button[aria-haspopup=dialog]").Click();
        inspector.Find("[role=dialog] button[aria-pressed=false]").Click();

        var selectedChoice = inspector.Find("[role=dialog] button[aria-pressed=true]");
        selectedChoice.TextContent.ShouldContain(compatibility.Reason);
        inspector.Find("[role=dialog] button[disabled]").Click();
        connection.ShouldBeNull();
        repair.ShouldBeNull();
        _ = inspector.Find("[role=dialog]");
    }

    [Test]
    public void TypedEditor_SourcePicker_EscapeCancelsAndReturnsFocus()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var nodes = CreatePickerNodes();
        AutomationConnectionRequest? connection = null;
        AutomationRepairConnectionRequest? repair = null;
        var inspector = context.Render<AutomationNodeInspector>(parameters =>
            parameters
                .Add(component => component.Node, nodes.Target)
                .Add(
                    component => component.Nodes,
                    [nodes.IncompatibleSource, nodes.CompatibleSource, nodes.Target]
                )
                .Add(component => component.Edges, [])
                .Add(component => component.Connect, request => connection = request)
                .Add(component => component.Repair, request => repair = request)
        );
        var opener = inspector.Find("button[aria-haspopup=dialog]");
        opener.Click();
        var focusCalls = FocusCalls(context);

        var dialog = inspector.Find("[role=dialog]");
        dialog.GetAttribute("aria-modal").ShouldBe("true");
        dialog.KeyDown(new KeyboardEventArgs { Key = "Escape" });

        connection.ShouldBeNull();
        repair.ShouldBeNull();
        inspector.FindAll("[role=dialog]").ShouldBeEmpty();
        FocusCalls(context).ShouldBe(focusCalls + 1);
    }

    [Test]
    public void TypedEditor_Inspector_DeclarationFieldsEditAddAndRemoveThroughStandardControls()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var registered = new CoreAutomationCatalogModule()
            .Definitions.Select(static definition => definition.Descriptor)
            .Single(static definition => definition.Id == AutomationDefinitionIds.CelTransform);
        var transform = AutomationEditorNode.Create(registered, new(new(48), new(72)));
        var inspector = context.Render<AutomationNodeInspector>(parameters =>
            parameters
                .Add(component => component.Node, transform)
                .Add(component => component.Nodes, [transform])
                .Add(component => component.Edges, [])
        );

        var declaredInput = transform.TransformInputs.Single();
        inspector
            .Find($"[id='automation-declaration-{declaredInput.PortId.Value}-required']")
            .Change(nameof(AutomationPortNullability.Nullable));
        transform.TransformInputs.Single().Nullability.ShouldBe(AutomationPortNullability.Nullable);
        inspector
            .Find($"[id='automation-declaration-{declaredInput.PortId.Value}-required']")
            .Change(nameof(AutomationPortNullability.NonNullable));
        transform
            .TransformInputs.Single()
            .Nullability.ShouldBe(AutomationPortNullability.NonNullable);

        inspector.Find("[aria-label='Add input']").Click();
        transform.TransformInputs.Count.ShouldBe(2);
        inspector.FindAll("[aria-label^='Remove input']")[1].Click();
        transform.TransformInputs.Single().PortId.ShouldBe(declaredInput.PortId);

        var declaredOutput = transform.TransformOutputs.Single();
        inspector
            .Find($"[id='automation-declaration-{declaredOutput.PortId.Value}-expression']")
            .Input("value > 10");
        transform.TransformOutputs.Single().Source.ShouldBe("value > 10");
    }

    [Test]
    public void TypedEditor_Inspector_RetainedConnectionShowsDiagnosticWithoutFillerProse()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var nodes = CreatePickerNodes();
        var retained = new AutomationFlowDraftEdge(
            Guid.NewGuid(),
            AutomationEdgeKind.Data,
            nodes.IncompatibleSource.Id,
            nodes.IncompatibleSource.Definition.Outputs.Single().Id,
            nodes.Target.Id,
            nodes.Target.Definition.Inputs.Single().Id
        );
        var inspector = context.Render<AutomationNodeInspector>(parameters =>
            parameters
                .Add(component => component.Node, nodes.Target)
                .Add(
                    component => component.Nodes,
                    [nodes.IncompatibleSource, nodes.CompatibleSource, nodes.Target]
                )
                .Add(component => component.Edges, [retained])
        );

        inspector
            .Find(".automation-connection-diagnostic[role=alert]")
            .TextContent.ShouldNotBeNullOrWhiteSpace();
        inspector
            .FindAll("button")
            .ShouldContain(button => button.TextContent.Trim() == "Repair source");
        inspector.Markup.ShouldNotContain("BlokeBot keeps");
        inspector.Markup.ShouldNotContain("unused choices");
    }

    [Test]
    public void TypedEditor_DynamicTransform_RoundTripsIdentityAndKeepsRemovedPortEdgesVisible()
    {
        var registered = new CoreAutomationCatalogModule()
            .Definitions.Select(static definition => definition.Descriptor)
            .Single(static definition => definition.Id == AutomationDefinitionIds.CelTransform);
        var transform = AutomationEditorNode.Create(registered, new(new(288), new(72)));
        var originalInput = transform.TransformInputs.Single();
        var originalOutput = transform.TransformOutputs.Single();
        transform.UpdateTransformInput(
            originalInput.PortId,
            "Amount",
            AutomationPortValueType.Number,
            AutomationPortNullability.Nullable
        );
        transform.UpdateTransformOutput(
            originalOutput.PortId,
            "Is high",
            AutomationPortValueType.Boolean,
            AutomationPortNullability.NonNullable,
            "value > 10"
        );
        var restored = AutomationEditorNode.Restore(transform.Draft(), transform.Definition);

        restored.TransformInputs.Single().PortId.ShouldBe(originalInput.PortId);
        restored.TransformInputs.Single().Identifier.ShouldBe(originalInput.Identifier);
        restored.TransformInputs.Single().BindingFieldId.ShouldBe(originalInput.BindingFieldId);
        restored.TransformOutputs.Single().PortId.ShouldBe(originalOutput.PortId);

        var source = AutomationEditorNode.Create(
            Definition(
                "number-source",
                AutomationNodeKind.Value,
                "Number source",
                outputs: [Port("number", AutomationPortValueType.Number)]
            ),
            new(new(48), new(72))
        );
        var retained = new AutomationFlowDraftEdge(
            Guid.NewGuid(),
            AutomationEdgeKind.Data,
            source.Id,
            new("number"),
            restored.Id,
            originalInput.PortId
        );
        restored.RemoveTransformInput(originalInput.PortId);

        AutomationConnections.Issue(retained, [source, restored]).ShouldNotBeNullOrWhiteSpace();
        retained.SourceNodeId.ShouldBe(source.Id);
        retained.SourcePortId.ShouldBe(new AutomationPortId("number"));
        retained.TargetNodeId.ShouldBe(restored.Id);
        retained.TargetPortId.ShouldBe(originalInput.PortId);
    }

    [Test]
    public void TypedEditor_CelCompletion_UsesDeclaredInputsAndArgumentsOnlyRestrictedScope()
    {
        var registered = new CoreAutomationCatalogModule()
            .Definitions.Select(static definition => definition.Descriptor)
            .Single(static definition => definition.Id == AutomationDefinitionIds.CelTransform);
        var transform = AutomationEditorNode.Create(registered, new(new(48), new(72)));
        var input = transform.TransformInputs.Single();
        transform.UpdateTransformInput(
            input.PortId,
            "Actor",
            AutomationPortValueType.Actor,
            AutomationPortNullability.NonNullable
        );
        transform.AddTransformInput();
        var arguments = transform.TransformInputs.Last();
        transform.UpdateTransformInput(
            arguments.PortId,
            "Arguments",
            AutomationPortValueType.Arguments,
            AutomationPortNullability.NonNullable
        );

        AutomationCelCompletions
            .ForOutput(transform)
            .Select(static item => item.Name)
            .ShouldBe(["value", "value.display_name", "value.login", arguments.Identifier.Value]);
        var argumentsPort = transform.Definition.Inputs.Single(port => port.Id == arguments.PortId);
        AutomationCelCompletions
            .ForRestrictedInput(argumentsPort)
            .ShouldBe([new("arguments", AutomationPortValueType.Arguments)]);
        AutomationCelCompletions
            .ForRestrictedInput(transform.Definition.Inputs.Single(port => port.Id == input.PortId))
            .ShouldBeEmpty();
    }

    private static AutomationDefinitionDescriptor Definition(
        string id,
        AutomationNodeKind kind,
        string name,
        IReadOnlyList<AutomationPortMetadata>? inputs = null,
        IReadOnlyList<AutomationPortMetadata>? outputs = null
    ) =>
        new(
            new(id),
            kind,
            AutomationDefinitionScope.Host,
            new(new(1), new(1)),
            new(name, $"Use {name} in this flow.", "Test"),
            [.. inputs ?? []],
            [.. outputs ?? []],
            inputs
                ?.Where(static input => input.BindingFieldId is not null)
                .Select(input => new AutomationConfigurationFieldMetadata(
                    input.BindingFieldId!.Value,
                    input.Name,
                    "Enter a value.",
                    new AutomationConfigurationFieldType.Data(input.ValueType),
                    true
                ))
                .ToImmutableArray()
                ?? [],
            AutomationActionCapabilities.None,
            AutomationActionRetrySafety.NotApplicable
        );

    private static AutomationPortMetadata Port(
        string id,
        AutomationPortValueType type,
        AutomationDataSensitivity sensitivity = AutomationDataSensitivity.Safe,
        AutomationPortNullability nullability = AutomationPortNullability.NonNullable,
        AutomationConfigurationFieldId? bindingFieldId = null
    ) => new(new(id), id, $"Supplies {type}.", type, sensitivity, nullability, bindingFieldId);

    private static string AccessiblePortName(AutomationPortMetadata port) =>
        port.ValueType == AutomationPortValueType.Flow
            ? port.Name
            : $"{port.Name} · {AutomationConnections.TypeLabel(port)}";

    private static PickerNodes CreatePickerNodes()
    {
        var incompatibleSource = AutomationEditorNode.Create(
            Definition(
                "number-source",
                AutomationNodeKind.Value,
                "Number source",
                outputs: [Port("number", AutomationPortValueType.Number)]
            ),
            new(new(48), new(72))
        );
        var compatibleSource = AutomationEditorNode.Create(
            Definition(
                "boolean-source",
                AutomationNodeKind.Value,
                "Boolean source",
                outputs: [Port("boolean", AutomationPortValueType.Boolean)]
            ),
            new(new(48), new(240))
        );
        var target = AutomationEditorNode.Create(
            Definition(
                "condition",
                AutomationNodeKind.Control,
                "Condition",
                inputs:
                [
                    Port(
                        "predicate",
                        AutomationPortValueType.Boolean,
                        bindingFieldId: new("predicate")
                    ),
                ]
            ),
            new(new(288), new(72))
        );
        target.SetBindingMode(new("predicate"), AutomationInputBindingMode.Connected);
        return new(incompatibleSource, compatibleSource, target);
    }

    private static PickerCompletion ActivatePickerAction(
        AutomationEditorNode target,
        IReadOnlyList<AutomationEditorNode> nodes,
        IReadOnlyList<AutomationFlowDraftEdge> edges,
        string actionName
    )
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        AutomationConnectionRequest? connection = null;
        AutomationRepairConnectionRequest? repair = null;
        var inspector = context.Render<AutomationNodeInspector>(parameters =>
            parameters
                .Add(component => component.Node, target)
                .Add(component => component.Nodes, nodes)
                .Add(component => component.Edges, edges)
                .Add(component => component.Connect, request => connection = request)
                .Add(component => component.Repair, request => repair = request)
        );
        var opener = inspector.Find("button[aria-haspopup=dialog]");
        opener.Click();
        inspector.Find("[role=dialog] button[aria-pressed=true]").Click();
        var focusCalls = FocusCalls(context);

        inspector
            .FindAll("button")
            .Single(button =>
                string.Equals(button.TextContent.Trim(), actionName, StringComparison.Ordinal)
            )
            .Click();

        inspector.FindAll("[role=dialog]").ShouldBeEmpty();
        FocusCalls(context).ShouldBe(focusCalls + 1);
        return new(connection, repair);
    }

    private static int FocusCalls(BunitContext context) =>
        context.JSInterop.Invocations.Count(static invocation =>
            invocation.Identifier == _focusInterop
        );

    private static AutomationConnectionRequest FlowConnection(
        AutomationEditorNode source,
        AutomationEditorNode target
    ) =>
        new(
            source.Id,
            source
                .Definition.Outputs.Single(static port =>
                    port.ValueType == AutomationPortValueType.Flow
                )
                .Id,
            target.Id,
            target
                .Definition.Inputs.Single(static port =>
                    port.ValueType == AutomationPortValueType.Flow
                )
                .Id
        );

    private static async Task DiscloseAsync(
        IRenderedComponent<AutomationEditorPage> page,
        AutomationNodeId nodeId
    )
    {
        await page.InvokeAsync(() =>
            page.FindComponent<AutomationFlowCanvas>()
                .Instance.ActivateNodeFromCanvasAsync(nodeId.Value)
        );
        page.WaitForAssertion(() => DisclosureCount(page).ShouldBe(1));
    }

    private static int DisclosureCount(IRenderedComponent<AutomationEditorPage> page) =>
        page.FindAll("[data-automation-node-select][aria-expanded='true']").Count;

    private static IElement NodeSelector(
        IRenderedComponent<AutomationEditorPage> page,
        AutomationNodeId nodeId
    ) => page.Find($"[data-automation-node='{nodeId.Value:D}'] [data-automation-node-select]");

    private static string CanvasDraft(AutomationFlowCanvas canvas) =>
        JsonSerializer.Serialize(
            new
            {
                Nodes = canvas.Nodes.Select(static node =>
                {
                    var draft = node.Draft();
                    return new
                    {
                        Id = draft.Id.Value,
                        draft.Definition.TypeId,
                        draft.Definition.SchemaVersion,
                        Configuration = draft.Definition.Configuration.GetRawText(),
                        Bindings = AutomationRuntimeSerialization.SerializeInputBindings(
                            draft.InputBindings
                        ),
                        ExpressionVersion = draft.ExpressionLanguageVersion.Value,
                        draft.FailurePolicy,
                        X = draft.Position.X.Value,
                        Y = draft.Position.Y.Value,
                        draft.DisplayAlias,
                    };
                }),
                Edges = canvas.Edges.Select(static edge => new
                {
                    edge.Id,
                    edge.Kind,
                    SourceNodeId = edge.SourceNodeId.Value,
                    SourcePortId = edge.SourcePortId.Value,
                    TargetNodeId = edge.TargetNodeId.Value,
                    TargetPortId = edge.TargetPortId.Value,
                }),
                canvas.Settings,
            }
        );

    private static IElement FlowStateButton(IRenderedComponent<AutomationEditorPage> page) =>
        page.FindAll(".automation-editor-actions button")
            .Single(button =>
                button.TextContent.Trim() is "Enable" or "Enable anyway" or "Disable"
            );

    private static AutomationDefinitionDescriptor[] ProductionDefinitions() =>
        [
            .. new CoreAutomationCatalogModule().Definitions.Select(static value =>
                value.Descriptor
            ),
            .. new TwitchEventAutomationCatalogModule().Definitions.Select(static value =>
                value.Descriptor
            ),
            .. new NativeOperationAutomationCatalogModule().Definitions.Select(static value =>
                value.Descriptor
            ),
            .. new CompetitionAutomationCatalogModule().Definitions.Select(static value =>
                value.Descriptor
            ),
        ];

    private sealed class AutomationEditorPageFixture : IAsyncDisposable
    {
        private AutomationEditorPageFixture(
            SqliteBlokeBotDbFactory database,
            BunitContext context,
            IRenderedComponent<AutomationEditorPage> page
        )
        {
            _database = database;
            _context = context;
            Page = page;
        }

        private readonly SqliteBlokeBotDbFactory _database;

        private readonly BunitContext _context;

        internal IRenderedComponent<AutomationEditorPage> Page { get; }

        internal static async Task<AutomationEditorPageFixture> CreateAsync(
            bool includeUnavailableFlow = false
        )
        {
            var database = await SqliteBlokeBotDbFactory.CreateAsync();
            var hostId = await SeedHostAsync(database);
            var context = UiTestContextFactory.Create(database, hostId);
            _ = context.Services.AddSingleton<IOverlayCueAdmissionService>(
                new UnavailableOverlayCueAdmissionService()
            );
            _ = context.Services.AddBlokeBotAutomations();
            var editor = AvailableEditor();
            _ = (
                await context
                    .Services.GetRequiredService<AutomationFlowService>()
                    .SaveAsync(editor.Draft(new(hostId)), CancellationToken.None)
            ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
            if (includeUnavailableFlow)
            {
                await SeedUnavailableFlowAsync(database, hostId);
            }

            var page = context.Render<AutomationEditorPage>();
            page.WaitForAssertion(() =>
                page.FindComponent<AutomationFlowCanvas>().Instance.Nodes.Count.ShouldBe(2)
            );
            return new(database, context, page);
        }

        private static async Task<int> SeedHostAsync(SqliteBlokeBotDbFactory database)
        {
            await using var db = await database.CreateDbContextAsync();
            var host = new BotHost
            {
                TwitchUserId = "automation-editor-host",
                Login = "streamer",
                DisplayName = "Streamer",
                EnabledFeatures = HostFeatureFlags.Automations,
                CreatedAtUtc = DateTime.UtcNow,
            };
            _ = db.Hosts.Add(host);
            _ = await db.SaveChangesAsync();
            return host.Id;
        }

        private static AutomationEditorState AvailableEditor()
        {
            var editor = AutomationEditorState.Create("Available flow");
            var definitions = ProductionDefinitions();
            var source = editor.AddNode(
                definitions.Single(static definition =>
                    definition.Id == AutomationDefinitionIds.StreamOnlineSource
                )
            );
            var target = editor.AddNode(
                definitions.Single(static definition =>
                    definition.Id == AutomationDefinitionIds.SendChatAction
                )
            );
            target.SetValue(new("message"), "Hello from the editor fixture.");
            var connection = FlowConnection(source, target);
            editor.Edges.Add(
                new(
                    Guid.NewGuid(),
                    AutomationEdgeKind.Flow,
                    connection.SourceNodeId,
                    connection.SourcePortId,
                    connection.TargetNodeId,
                    connection.TargetPortId
                )
            );
            return editor;
        }

        private static async Task SeedUnavailableFlowAsync(
            SqliteBlokeBotDbFactory database,
            int hostId
        )
        {
            await using var db = await database.CreateDbContextAsync();
            var flowId = Guid.NewGuid();
            var updatedAtUtc = new DateTime(2026, 7, 9, 12, 0, 0, DateTimeKind.Utc);
            _ = db.AutomationFlows.Add(
                new()
                {
                    Id = flowId,
                    HostId = hostId,
                    Name = "Unavailable provider flow",
                    SchemaVersion = 1,
                    CreatedAtUtc = updatedAtUtc,
                    UpdatedAtUtc = updatedAtUtc,
                    Nodes =
                    [
                        new()
                        {
                            Id = Guid.NewGuid(),
                            FlowId = flowId,
                            DefinitionId = "removed-provider-node",
                            DefinitionSchemaVersion = 1,
                            ConfigurationJson = "{}",
                            InputBindingsJson =
                                AutomationRuntimeSerialization.SerializeInputBindings(
                                    ImmutableDictionary<
                                        AutomationConfigurationFieldId,
                                        AutomationInputBinding
                                    >.Empty
                                ),
                            ExpressionLanguageVersion = AutomationExpressionLanguage
                                .CurrentVersion
                                .Value,
                            CanvasX = 48,
                            CanvasY = 72,
                        },
                    ],
                }
            );
            _ = await db.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            _context.Dispose();
            await _database.DisposeAsync();
        }
    }

    private sealed record PickerNodes(
        AutomationEditorNode IncompatibleSource,
        AutomationEditorNode CompatibleSource,
        AutomationEditorNode Target
    );

    private sealed record PickerCompletion(
        AutomationConnectionRequest? Connection,
        AutomationRepairConnectionRequest? Repair
    );
}
