using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.Automations.Page;
using Microsoft.AspNetCore.Components.Web;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class AutomationEditorHistoryTests
{
    [Test]
    public void History_AtCapacity_EvictsOnlyTheOldestDiffAndTransfersInLifoOrder()
    {
        var editor = AutomationEditorState.Create("Initial");
        var history = new AutomationEditorHistory();
        history.Reset(editor);

        for (var index = 1; index <= AutomationEditorHistory.Capacity + 1; index++)
        {
            editor.Name = $"Flow {index}";
            history.Record(editor).ShouldBeTrue();
        }

        history.UndoCount.ShouldBe(AutomationEditorHistory.Capacity);
        history.RedoCount.ShouldBe(0);
        for (var index = AutomationEditorHistory.Capacity + 1; index >= 2; index--)
        {
            editor = history.Undo(editor).ShouldNotBeNull();
            editor.Name.ShouldBe($"Flow {index - 1}");
        }

        history.Undo(editor).ShouldBeNull();
        history.UndoCount.ShouldBe(0);
        history.RedoCount.ShouldBe(AutomationEditorHistory.Capacity);
        for (var index = 2; index <= AutomationEditorHistory.Capacity + 1; index++)
        {
            editor = history.Redo(editor).ShouldNotBeNull();
            editor.Name.ShouldBe($"Flow {index}");
        }

        history.Redo(editor).ShouldBeNull();
        history.UndoCount.ShouldBe(AutomationEditorHistory.Capacity);
        history.RedoCount.ShouldBe(0);
    }

    [Test]
    public void History_NoOpsAndEmptyStacksDoNothing_WhileNewAuthoringClearsAllRedo()
    {
        var editor = AutomationEditorState.Create("Initial");
        var history = new AutomationEditorHistory();
        history.Reset(editor);

        history.Undo(editor).ShouldBeNull();
        history.Redo(editor).ShouldBeNull();
        editor.Name = "Initial";
        history.Record(editor).ShouldBeFalse();
        editor.RemoveNode(new(Guid.NewGuid()));
        history.Record(editor).ShouldBeFalse();

        editor.Name = "First";
        history.Record(editor).ShouldBeTrue();
        editor.Name = "Second";
        history.Record(editor).ShouldBeTrue();
        editor = history.Undo(editor).ShouldNotBeNull();
        history.RedoCount.ShouldBe(1);

        editor.Name = "Replacement";
        history.Record(editor).ShouldBeTrue();

        history.RedoCount.ShouldBe(0);
        history.Redo(editor).ShouldBeNull();
        editor = history.Undo(editor).ShouldNotBeNull();
        editor.Name.ShouldBe("First");
    }

    [Test]
    public void History_MultiNodeMoveAndDeletionWithIncidentEdges_AreEachOneAtomicDiff()
    {
        var definition = Definition(
            "flow-node",
            AutomationNodeKind.Action,
            inputs: [Port("in", AutomationPortValueType.Flow)],
            outputs: [Port("out", AutomationPortValueType.Flow)]
        );
        var editor = AutomationEditorState.Create("Atomic graph");
        var first = editor.AddNode(definition);
        var second = editor.AddNode(definition);
        var edge = new AutomationFlowDraftEdge(
            Guid.NewGuid(),
            AutomationEdgeKind.Flow,
            first.Id,
            new("out"),
            second.Id,
            new("in")
        );
        editor.Edges.Add(edge);
        var originalFirstPosition = first.Position;
        var originalSecondPosition = second.Position;
        var history = new AutomationEditorHistory();
        history.Reset(editor);

        first.Position = new(new(400), new(500));
        second.Position = new(new(600), new(700));
        history.Record(editor).ShouldBeTrue();
        history.UndoCount.ShouldBe(1);

        editor = history.Undo(editor).ShouldNotBeNull();
        editor.Nodes.Single(node => node.Id == first.Id).Position.ShouldBe(originalFirstPosition);
        editor.Nodes.Single(node => node.Id == second.Id).Position.ShouldBe(originalSecondPosition);
        editor = history.Redo(editor).ShouldNotBeNull();
        editor
            .Nodes.Single(node => node.Id == first.Id)
            .Position.ShouldBe(new AutomationCanvasPosition(new(400), new(500)));
        editor
            .Nodes.Single(node => node.Id == second.Id)
            .Position.ShouldBe(new AutomationCanvasPosition(new(600), new(700)));

        editor.RemoveNode(first.Id);
        editor.RemoveNode(second.Id);
        history.Record(editor).ShouldBeTrue();
        history.UndoCount.ShouldBe(2);
        editor.Nodes.ShouldBeEmpty();
        editor.Edges.ShouldBeEmpty();

        editor = history.Undo(editor).ShouldNotBeNull();
        editor.Nodes.Select(static node => node.Id).ShouldBe([first.Id, second.Id]);
        editor.Edges.ShouldBe([edge]);
        editor = history.Redo(editor).ShouldNotBeNull();
        editor.Nodes.ShouldBeEmpty();
        editor.Edges.ShouldBeEmpty();
    }

    [Test]
    public void History_NodeAliasFailurePolicyAndPersistedCanvasSettings_RoundTripExactly()
    {
        var definition = new CoreAutomationCatalogModule()
            .Definitions.Select(static definition => definition.Descriptor)
            .Single(static definition => definition.Id == AutomationDefinitionIds.SendChatAction);
        var editor = AutomationEditorState.Create("Settings");
        var node = editor.AddNode(definition);
        var history = new AutomationEditorHistory();
        history.Reset(editor);

        node.SetDisplayAlias("Announce result");
        history.Record(editor).ShouldBeTrue();
        node.FailurePolicy = AutomationNodeFailurePolicy.Continue;
        history.Record(editor).ShouldBeTrue();
        editor.Canvas = new(AutomationFlowOrientation.Vertical, AutomationEdgeStyle.Smooth);
        history.Record(editor).ShouldBeTrue();

        editor = history.Undo(editor).ShouldNotBeNull();
        editor.Canvas.ShouldBe(default);
        node = editor.Nodes.Single();
        node.FailurePolicy.ShouldBe(AutomationNodeFailurePolicy.Continue);
        node.DisplayAlias.ShouldBe("Announce result");
        editor = history.Undo(editor).ShouldNotBeNull();
        node = editor.Nodes.Single();
        node.FailurePolicy.ShouldBe(AutomationNodeFailurePolicy.Stop);
        node.DisplayAlias.ShouldBe("Announce result");
        editor = history.Undo(editor).ShouldNotBeNull();
        editor.Nodes.Single().DisplayAlias.ShouldBeNull();

        editor = history.Redo(editor).ShouldNotBeNull();
        editor = history.Redo(editor).ShouldNotBeNull();
        editor = history.Redo(editor).ShouldNotBeNull();
        node = editor.Nodes.Single();
        node.DisplayAlias.ShouldBe("Announce result");
        node.FailurePolicy.ShouldBe(AutomationNodeFailurePolicy.Continue);
        editor.Canvas.ShouldBe(
            new AutomationFlowCanvasSettings(
                AutomationFlowOrientation.Vertical,
                AutomationEdgeStyle.Smooth
            )
        );
    }

    [Test]
    public void History_TransformSchemaBindingsCelAndRetainedRepair_RestoreExactStableIdentity()
    {
        var transformDefinition = new CoreAutomationCatalogModule()
            .Definitions.Select(static definition => definition.Descriptor)
            .Single(static definition => definition.Id == AutomationDefinitionIds.CelTransform);
        var sourceDefinition = Definition(
            "actor-source",
            AutomationNodeKind.Value,
            outputs: [Port("actor", AutomationPortValueType.Actor)]
        );
        var editor = AutomationEditorState.Create("Transform history");
        var source = editor.AddNode(sourceDefinition);
        var transform = editor.AddNode(transformDefinition);
        var originalInput = transform.TransformInputs.Single();
        var originalOutput = transform.TransformOutputs.Single();
        var retainedEdge = new AutomationFlowDraftEdge(
            Guid.NewGuid(),
            AutomationEdgeKind.Data,
            source.Id,
            new("actor"),
            transform.Id,
            originalInput.PortId
        );
        editor.Edges.Add(retainedEdge);
        var history = new AutomationEditorHistory();
        history.Reset(editor);

        transform.UpdateTransformInput(
            originalInput.PortId,
            "Arguments",
            AutomationPortValueType.Arguments,
            AutomationPortNullability.Nullable
        );
        transform.SetValue(originalInput.BindingFieldId, "[]");
        transform.SetExpression(
            originalInput.BindingFieldId,
            new(AutomationExpressionLanguage.CurrentVersion, "arguments")
        );
        transform.SetBindingMode(
            originalInput.BindingFieldId,
            AutomationInputBindingMode.Expression
        );
        transform.UpdateTransformOutput(
            originalOutput.PortId,
            "First argument",
            AutomationPortValueType.Text,
            AutomationPortNullability.Nullable,
            "value[0]"
        );
        history.Record(editor).ShouldBeTrue();

        transform.AddTransformInput();
        transform.AddTransformOutput();
        var addedInput = transform.TransformInputs.Last();
        var addedOutput = transform.TransformOutputs.Last();
        transform.UpdateTransformInput(
            addedInput.PortId,
            "Actor",
            AutomationPortValueType.Actor,
            AutomationPortNullability.NonNullable
        );
        transform.SetBindingMode(addedInput.BindingFieldId, AutomationInputBindingMode.Connected);
        transform.UpdateTransformOutput(
            addedOutput.PortId,
            "Display name",
            AutomationPortValueType.Text,
            AutomationPortNullability.NonNullable,
            $"{addedInput.Identifier.Value}.display_name"
        );
        history.Record(editor).ShouldBeTrue();

        editor.Edges[0] = retainedEdge with { TargetPortId = addedInput.PortId };
        history.Record(editor).ShouldBeTrue();
        AutomationConnections.Issue(editor.Edges.Single(), editor.Nodes).ShouldBeNull();

        transform.RemoveTransformInput(addedInput.PortId);
        transform.RemoveTransformOutput(addedOutput.PortId);
        history.Record(editor).ShouldBeTrue();
        history.UndoCount.ShouldBe(4);
        AutomationConnections
            .Issue(editor.Edges.Single(), editor.Nodes)
            .ShouldNotBeNullOrWhiteSpace();

        editor = history.Undo(editor).ShouldNotBeNull();
        transform = editor.Nodes.Single(node => node.Id == transform.Id);
        transform
            .TransformInputs.Select(static input => input.PortId)
            .ShouldBe([originalInput.PortId, addedInput.PortId]);
        transform
            .TransformOutputs.Select(static output => output.PortId)
            .ShouldBe([originalOutput.PortId, addedOutput.PortId]);
        editor.Edges.ShouldBe([retainedEdge with { TargetPortId = addedInput.PortId }]);
        AutomationConnections.Issue(editor.Edges.Single(), editor.Nodes).ShouldBeNull();

        editor = history.Undo(editor).ShouldNotBeNull();
        transform = editor.Nodes.Single(node => node.Id == transform.Id);
        editor.Edges.ShouldBe([retainedEdge]);
        AutomationConnections.Issue(retainedEdge, editor.Nodes).ShouldNotBeNullOrWhiteSpace();
        var restoredInput = transform.TransformInputs.Single(input =>
            input.PortId == originalInput.PortId
        );
        restoredInput.Identifier.ShouldBe(originalInput.Identifier);
        restoredInput.BindingFieldId.ShouldBe(originalInput.BindingFieldId);
        transform.TransformOutputs.ShouldContain(output => output.PortId == originalOutput.PortId);
        transform
            .Binding(originalInput.BindingFieldId)
            .ShouldBe(
                new(
                    AutomationInputBindingMode.Expression,
                    new(AutomationExpressionLanguage.CurrentVersion, "arguments")
                )
            );
        transform
            .TransformOutputs.Single(output => output.PortId == originalOutput.PortId)
            .Source.ShouldBe("value[0]");
        transform
            .TransformOutputs.Single(output => output.PortId == addedOutput.PortId)
            .Source.ShouldBe($"{addedInput.Identifier.Value}.display_name");

        editor = history.Undo(editor).ShouldNotBeNull();
        transform = editor.Nodes.Single(node => node.Id == transform.Id);
        transform.TransformInputs.ShouldNotContain(input => input.PortId == addedInput.PortId);
        transform.TransformOutputs.ShouldNotContain(output => output.PortId == addedOutput.PortId);
        editor = history.Redo(editor).ShouldNotBeNull();
        transform = editor.Nodes.Single(node => node.Id == transform.Id);
        transform.TransformInputs.ShouldContain(input =>
            input.PortId == addedInput.PortId
            && input.Identifier == addedInput.Identifier
            && input.BindingFieldId == addedInput.BindingFieldId
        );
        transform.TransformOutputs.ShouldContain(output => output.PortId == addedOutput.PortId);
        editor = history.Redo(editor).ShouldNotBeNull();
        editor.Edges.ShouldBe([retainedEdge with { TargetPortId = addedInput.PortId }]);
        AutomationConnections.Issue(editor.Edges.Single(), editor.Nodes).ShouldBeNull();
        editor = history.Redo(editor).ShouldNotBeNull();
        transform = editor.Nodes.Single(node => node.Id == transform.Id);
        transform.TransformInputs.ShouldNotContain(input => input.PortId == addedInput.PortId);
        transform.TransformOutputs.ShouldNotContain(output => output.PortId == addedOutput.PortId);
        editor.Edges.ShouldBe([retainedEdge with { TargetPortId = addedInput.PortId }]);
        AutomationConnections
            .Issue(editor.Edges.Single(), editor.Nodes)
            .ShouldNotBeNullOrWhiteSpace();
    }

    [Test]
    public void History_UnifiedSendAndConditionPayloads_RoundTripWithoutDuplicateState()
    {
        var definitions = new CoreAutomationCatalogModule()
            .Definitions.Select(static definition => definition.Descriptor)
            .ToArray();
        var editor = AutomationEditorState.Create("Binding history");
        var send = editor.AddNode(
            definitions.Single(static definition =>
                definition.Id == AutomationDefinitionIds.SendChatAction
            )
        );
        var condition = editor.AddNode(
            definitions.Single(static definition =>
                definition.Id == AutomationDefinitionIds.ConditionControl
            )
        );
        var sendField = new AutomationConfigurationFieldId("message");
        var conditionField = new AutomationConfigurationFieldId("predicate");
        var history = new AutomationEditorHistory();
        history.Reset(editor);

        send.SetValue(sendField, "Fixed message");
        history.Record(editor).ShouldBeTrue();
        send.SetExpression(
            sendField,
            new(AutomationExpressionLanguage.CurrentVersion, "\"Expression message\"")
        );
        history.Record(editor).ShouldBeTrue();
        send.SetBindingMode(sendField, AutomationInputBindingMode.Expression);
        history.Record(editor).ShouldBeTrue();
        condition.SetValue(conditionField, bool.TrueString);
        history.Record(editor).ShouldBeTrue();
        condition.SetExpression(
            conditionField,
            new(AutomationExpressionLanguage.CurrentVersion, "true")
        );
        history.Record(editor).ShouldBeTrue();
        condition.SetBindingMode(conditionField, AutomationInputBindingMode.Connected);
        history.Record(editor).ShouldBeTrue();

        for (var index = 0; index < 6; index++)
        {
            editor = history.Undo(editor).ShouldNotBeNull();
        }

        send = editor.Nodes.Single(node => node.Id == send.Id);
        condition = editor.Nodes.Single(node => node.Id == condition.Id);
        send.Value(sendField).ShouldBeEmpty();
        send.Binding(sendField).ShouldBe(new(AutomationInputBindingMode.Fixed, null));
        condition.Value(conditionField).ShouldBe(bool.FalseString);
        condition.Binding(conditionField).ShouldBe(new(AutomationInputBindingMode.Fixed, null));

        for (var index = 0; index < 6; index++)
        {
            editor = history.Redo(editor).ShouldNotBeNull();
        }

        send = editor.Nodes.Single(node => node.Id == send.Id);
        condition = editor.Nodes.Single(node => node.Id == condition.Id);
        send.Value(sendField).ShouldBe("Fixed message");
        send.Binding(sendField)
            .ShouldBe(
                new(
                    AutomationInputBindingMode.Expression,
                    new(AutomationExpressionLanguage.CurrentVersion, "\"Expression message\"")
                )
            );
        condition.Value(conditionField).ShouldBe(bool.TrueString);
        condition
            .Binding(conditionField)
            .ShouldBe(
                new(
                    AutomationInputBindingMode.Connected,
                    new(AutomationExpressionLanguage.CurrentVersion, "true")
                )
            );
        editor.Nodes.Count.ShouldBe(2);
        send.Draft().InputBindings.Count.ShouldBe(1);
        condition.Draft().InputBindings.Count.ShouldBe(1);
    }

    [Test]
    public void History_SaveBaselineRetainsTransfersAndFlowIdentity_WhileResetClearsBothStacks()
    {
        var editor = AutomationEditorState.Create("Before save");
        var history = new AutomationEditorHistory();
        history.Reset(editor);
        editor.Name = "Saved name";
        history.Record(editor).ShouldBeTrue();
        var savedFlowId = new AutomationFlowId(Guid.NewGuid());
        editor.Id = savedFlowId;
        history.ContinueWith(editor);
        var saved = AutomationEditorDraftSnapshot.Capture(editor);

        history.UndoCount.ShouldBe(1);
        editor = history.Undo(editor).ShouldNotBeNull();
        editor.Id.ShouldBe(savedFlowId);
        saved.Matches(editor).ShouldBeFalse();
        editor = history.Redo(editor).ShouldNotBeNull();
        editor.Id.ShouldBe(savedFlowId);
        saved.Matches(editor).ShouldBeTrue();

        editor.Name = "Unsaved again";
        history.Record(editor).ShouldBeTrue();
        saved.Matches(editor).ShouldBeFalse();
        history.Reset(editor);

        history.UndoCount.ShouldBe(0);
        history.RedoCount.ShouldBe(0);
        history.Undo(editor).ShouldBeNull();
        history.Redo(editor).ShouldBeNull();
    }

    [Test]
    public void HistoryShortcuts_CtrlZAndCtrlYTransferExactlyOneDiff_WhileCtrlRDoesNothing()
    {
        var editor = AutomationEditorState.Create("Initial");
        var history = new AutomationEditorHistory();
        history.Reset(editor);
        editor.Name = "First";
        history.Record(editor).ShouldBeTrue();
        editor.Name = "Second";
        history.Record(editor).ShouldBeTrue();

        editor = ApplyShortcut(
            history,
            editor,
            new KeyboardEventArgs { Key = "z", CtrlKey = true }
        );
        editor.Name.ShouldBe("First");
        history.UndoCount.ShouldBe(1);
        history.RedoCount.ShouldBe(1);
        editor = ApplyShortcut(
            history,
            editor,
            new KeyboardEventArgs { Key = "Y", CtrlKey = true }
        );
        editor.Name.ShouldBe("Second");
        history.UndoCount.ShouldBe(2);
        history.RedoCount.ShouldBe(0);

        editor = ApplyShortcut(
            history,
            editor,
            new KeyboardEventArgs { Key = "r", CtrlKey = true }
        );
        editor.Name.ShouldBe("Second");
        history.UndoCount.ShouldBe(2);
        history.RedoCount.ShouldBe(0);
        AutomationEditorHistoryShortcut
            .Resolve(
                new KeyboardEventArgs
                {
                    Key = "z",
                    CtrlKey = true,
                    ShiftKey = true,
                }
            )
            .ShouldBe(AutomationEditorHistoryAction.None);
        AutomationEditorHistoryShortcut
            .Resolve(new KeyboardEventArgs { Key = "y", MetaKey = true })
            .ShouldBe(AutomationEditorHistoryAction.None);
    }

    private static AutomationEditorState ApplyShortcut(
        AutomationEditorHistory history,
        AutomationEditorState editor,
        KeyboardEventArgs args
    ) =>
        AutomationEditorHistoryShortcut.Resolve(args) switch
        {
            AutomationEditorHistoryAction.Undo => history.Undo(editor) ?? editor,
            AutomationEditorHistoryAction.Redo => history.Redo(editor) ?? editor,
            _ => editor,
        };

    private static AutomationDefinitionDescriptor Definition(
        string id,
        AutomationNodeKind kind,
        IReadOnlyList<AutomationPortMetadata>? inputs = null,
        IReadOnlyList<AutomationPortMetadata>? outputs = null
    ) =>
        new(
            new(id),
            kind,
            AutomationDefinitionScope.Host,
            new(new(1), new(1)),
            new(id, $"Use {id}.", "Test"),
            [.. inputs ?? []],
            [.. outputs ?? []],
            [],
            AutomationActionCapabilities.None,
            AutomationActionRetrySafety.NotApplicable
        );

    private static AutomationPortMetadata Port(string id, AutomationPortValueType type) =>
        new(
            new(id),
            id,
            $"Supplies {type}.",
            type,
            AutomationDataSensitivity.Safe,
            AutomationPortNullability.NonNullable
        );
}
