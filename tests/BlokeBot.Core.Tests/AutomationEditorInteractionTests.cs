using System.Collections.Immutable;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.Automations.Page;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class AutomationEditorInteractionTests
{
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
        results[1].Availability.ShouldBe("BlokeBot needs permission to read chat events.");

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

        toolbox
            .Find("#automation-toolbox-tab-transforms")
            .GetAttribute("aria-selected")
            .ShouldBe("true");
        toolbox.Find("[data-automation-toolbox]").KeyDown(new KeyboardEventArgs { Key = "Escape" });
        closed.ShouldBe(1);
    }

    [Test]
    public void TypedEditor_EditorState_AddsAtTheFirstFreeDeterministicGridPosition()
    {
        var definition = Definition("value", AutomationNodeKind.Value, "Value");
        var editor = AutomationEditorState.Create("Flow");
        var first = editor.AddNode(definition);
        var second = editor.AddNode(definition);
        var third = editor.AddNode(definition);
        editor.RemoveNode(second.Id);

        var replacement = editor.AddNode(definition);

        first.Position.ShouldBe(new AutomationCanvasPosition(new(48), new(72)));
        third.Position.ShouldBe(new AutomationCanvasPosition(new(528), new(72)));
        replacement.Position.ShouldBe(new AutomationCanvasPosition(new(288), new(72)));
    }

    [Test]
    public void TypedEditor_TypedConnections_AllowFanOutAndRetainAnExactInvalidEdgeForRepair()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
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

        AutomationConnections
            .Issue(edges[0], [source, firstTarget, secondTarget])
            .ShouldBe("Expected Boolean. The selected source port supplies Number.");
        var canvas = context.Render<AutomationFlowCanvas>(parameters =>
            parameters
                .Add(component => component.Nodes, [source, firstTarget, secondTarget])
                .Add(component => component.Edges, edges)
                .Add(component => component.ViewportKey, "typed")
        );

        canvas.FindAll("[data-edge-kind=data]").Count.ShouldBe(2);
        canvas.FindAll(".automation-edge-group--invalid").Count.ShouldBe(2);
        canvas.FindAll("[data-port-direction=output][data-port-type=Number]").Count.ShouldBe(1);
        canvas
            .Find("[data-automation-node-select]")
            .GetAttribute("aria-label")
            .ShouldNotBeNull()
            .ShouldContain("number · Number output");
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

        AutomationConnections
            .Compatibility(AutomationNodeKind.Value, safeNullableNumber, safeRequiredNumber)
            .Reason.ShouldBe("Expected Number. The selected source can be null.");
        AutomationConnections
            .Compatibility(AutomationNodeKind.Value, sensitiveNumber, safeRequiredNumber)
            .Reason.ShouldBe("This input cannot accept Sensitive Data.");
        AutomationConnections
            .Compatibility(AutomationNodeKind.Value, safeRequiredNumber, sensitiveNumber)
            .IsCompatible.ShouldBeTrue();
        AutomationConnections
            .Compatibility(AutomationNodeKind.Action, safeRequiredNumber, safeRequiredNumber)
            .IsCompatible.ShouldBeFalse();
    }

    [Test]
    public void TypedEditor_SourcePicker_ExplainsCompatibilityAndEscapeCancelsTheChoice()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var numberSource = AutomationEditorNode.Create(
            Definition(
                "number-source",
                AutomationNodeKind.Value,
                "Number source",
                outputs: [Port("number", AutomationPortValueType.Number)]
            ),
            new(new(48), new(72))
        );
        var booleanSource = AutomationEditorNode.Create(
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
        var inspector = context.Render<AutomationNodeInspector>(parameters =>
            parameters
                .Add(component => component.Node, target)
                .Add(component => component.Nodes, [numberSource, booleanSource, target])
                .Add(component => component.Edges, [])
        );

        inspector.FindAll("button").Single(button => button.TextContent == "Choose source").Click();

        var dialog = inspector.Find("[role=dialog]");
        dialog.TextContent.ShouldContain("Number source · number");
        dialog.TextContent.ShouldContain(
            "Expected Boolean. The selected source port supplies Number."
        );
        dialog.TextContent.ShouldContain("Boolean source · boolean");
        dialog.KeyDown(new KeyboardEventArgs { Key = "Escape" });
        inspector.FindAll("[role=dialog]").ShouldBeEmpty();
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

        AutomationConnections
            .Issue(retained, [source, restored])
            .ShouldBe("A saved port is not available.");
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

    [Test]
    public void TypedEditor_ListView_ShowsEveryNodeAndStatesThatConnectionsDoNotDefineRunOrder()
    {
        using var context = new BunitContext();
        var definition = Definition("value", AutomationNodeKind.Value, "Value");
        var first = AutomationEditorNode.Create(definition, new(new(48), new(72)));
        var second = AutomationEditorNode.Create(definition, new(new(288), new(72)));
        var list = context.Render<AutomationFlowList>(parameters =>
            parameters
                .Add(component => component.Nodes, [first, second])
                .Add(component => component.Edges, [])
        );

        list.FindAll("[data-automation-list-node]").Count.ShouldBe(2);
        list.Find("[data-automation-list]")
            .TextContent.ShouldContain("Connections do not define run order.");
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
}
