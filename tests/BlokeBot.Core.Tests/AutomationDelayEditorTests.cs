using System.Collections.Immutable;
using System.Text.Json;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.Automations.Page;
using Bunit;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class AutomationEditorInteractionTests
{
    [Test]
    public void DelayBinding_InspectorLegacyEditsAndExplicitActivationUndoRedoPreserveActualMaps()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var definition = new CoreAutomationCatalogModule()
            .Definitions.Single(value =>
                value.Descriptor.Id == AutomationDefinitionIds.DelayControl
            )
            .Descriptor;
        var oldBinding = new AutomationInputBinding(
            AutomationInputBindingMode.Connected,
            new(AutomationExpressionLanguage.CurrentVersion, "99999")
        );
        var legacy = new AutomationFlowDraftNode(
            new(Guid.NewGuid()),
            new(
                "delay",
                1,
                JsonSerializer.Deserialize<JsonElement>("""{"duration-milliseconds":17}""")
            ),
            AutomationExpressionLanguage.CurrentVersion,
            AutomationNodeFailurePolicy.Stop,
            ImmutableDictionary<AutomationConfigurationFieldId, AutomationInputBinding>.Empty.Add(
                AutomationDelayDurationBinding.LiteralField,
                oldBinding
            )
        );
        var editor = AutomationEditorState.Restore(
            new AutomationFlowDraft(
                null,
                new(1),
                "legacy",
                AutomationFlowSchema.CurrentVersion,
                false,
                [legacy],
                []
            ),
            new Dictionary<AutomationNodeId, AutomationDefinitionDescriptor>
            {
                [legacy.Id] = definition,
            }
        );
        var history = new AutomationEditorHistory();
        history.StartNew(editor);
        var inspector = context.Render<AutomationNodeInspector>(parameters =>
            parameters
                .Add(component => component.Node, editor.Nodes[0])
                .Add(component => component.Nodes, editor.Nodes)
                .Add(component => component.Changed, () => history.Record(editor))
        );
        var fixedInput = inspector.Find(".automation-input-editor input[type=number]");
        fixedInput.Input("19");
        editor.Draft(new(1)).Nodes[0].InputBindings.ShouldBe(legacy.InputBindings);
        fixedInput.GetAttribute("min").ShouldBe("1");
        fixedInput.GetAttribute("step").ShouldBe("1");
        inspector
            .FindAll(".automation-binding-mode-tabs button")
            .Single(button => button.TextContent == "Expression")
            .Click();
        var activated = editor.Draft(new(1)).Nodes[0];
        activated.InputBindings[AutomationDelayDurationBinding.LiteralField].ShouldBe(oldBinding);
        activated
            .InputBindings[AutomationDelayDurationBinding.ValueField]
            .Mode.ShouldBe(AutomationInputBindingMode.Expression);
        activated
            .InputBindings[AutomationDelayDurationBinding.ValueField]
            .Expression.ShouldBeNull();
        inspector
            .Find("textarea[aria-label='Duration (milliseconds) input expression']")
            .Input("int(arguments[0])");
        editor = history.Undo(editor).ShouldNotBeNull();
        editor = history.Undo(editor).ShouldNotBeNull();
        editor
            .Draft(new(1))
            .Nodes[0]
            .InputBindings.ContainsKey(AutomationDelayDurationBinding.ValueField)
            .ShouldBeFalse();
        editor.Nodes[0].Value(AutomationDelayDurationBinding.ValueField).ShouldBe("19");
        editor = history.Redo(editor).ShouldNotBeNull();
        editor = history.Redo(editor).ShouldNotBeNull();
        var redo = editor.Draft(new(1)).Nodes[0];
        redo.InputBindings[AutomationDelayDurationBinding.ValueField]
            .Expression!.Source.ShouldBe("int(arguments[0])");
        redo.InputBindings[AutomationDelayDurationBinding.LiteralField].ShouldBe(oldBinding);
        redo.Definition.Configuration.EnumerateObject()
            .Select(property => property.Name)
            .ShouldBe(["duration-milliseconds"]);
    }

    [Test]
    [Arguments(AutomationInputBindingMode.Fixed)]
    [Arguments(AutomationInputBindingMode.Expression)]
    [Arguments(AutomationInputBindingMode.Connected)]
    public void DelayBinding_InspectorShowsHiddenLiteralErrorsOnTheSingleDurationControl(
        AutomationInputBindingMode mode
    )
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var definition = new CoreAutomationCatalogModule()
            .Definitions.Single(value =>
                value.Descriptor.Id == AutomationDefinitionIds.DelayControl
            )
            .Descriptor;
        var node = AutomationEditorNode.Create(definition, default);
        node.Value(AutomationDelayDurationBinding.ValueField).ShouldBe("1");
        node.Draft()
            .InputBindings[AutomationDelayDurationBinding.ValueField]
            .Mode.ShouldBe(AutomationInputBindingMode.Fixed);
        node.SetBindingMode(AutomationDelayDurationBinding.ValueField, mode);
        var rendered = context.Render<AutomationNodeInspector>(parameters =>
            parameters
                .Add(component => component.Node, node)
                .Add(component => component.Nodes, [node])
                .Add(
                    component => component.Errors,
                    [
                        new(
                            node.Id,
                            "duration-invalid",
                            "Enter a positive whole number of milliseconds.",
                            AutomationDelayDurationBinding.LiteralField
                        ),
                    ]
                )
        );
        rendered
            .Find(".automation-input-editor [role=alert]")
            .TextContent.ShouldBe("Enter a positive whole number of milliseconds.");
        rendered
            .FindAll(".automation-field input[type=number]")
            .ShouldNotContain(input =>
                input.Id != null
                && input.Id.EndsWith("duration-milliseconds", StringComparison.Ordinal)
            );
    }
}
