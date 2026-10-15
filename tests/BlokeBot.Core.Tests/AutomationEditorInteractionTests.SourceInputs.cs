using System.Collections.Immutable;
using System.Text.Json;
using BlokeBot.Core.Components;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.Automations.Page;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class AutomationEditorInteractionTests
{
    [Test]
    [Arguments("arguments", AutomationPortValueType.Arguments)]
    [Arguments("argument.seconds", AutomationPortValueType.Number)]
    [Arguments("argument.name", AutomationPortValueType.Text)]
    public async Task SourceInputs_CreateUndoRedoRenameAndSavedReopenKeepOneInheritedContract(
        string sourcePort,
        AutomationPortValueType expectedType
    )
    {
        await using var f = await AutomationEditorPageFixture.CreateAsync(typedCommand: true);
        var transform = await AddSourceInputTransform(f.Page);
        var source = f
            .Page.FindComponent<AutomationFlowCanvas>()
            .Instance.Nodes.Single(node =>
                node.Definition.Id == AutomationDefinitionIds.CustomCommandSource
            );
        var originalInputs = transform.TransformInputs.ToArray();
        var originalEdges = f.Page.FindComponent<AutomationFlowCanvas>().Instance.Edges.ToArray();
        var inspector = f.Page.FindComponent<AutomationNodeInspector>();
        inspector.Find("button[aria-label='Add input from source']").Click();
        ChooseSourceRadio(
                inspector,
                source,
                source.Definition.Outputs.Single(port => port.Id.Value == sourcePort)
            )
            .Click();
        inspector.Find("#automation-new-input-name").Input("Bound input");
        inspector.Find("#automation-new-input-identifier").Input("bound_input");
        inspector.Find("[data-automation-source-picker-complete]").Click();
        var input = transform.TransformInputs.Last();
        input.ValueType.ShouldBe(expectedType);
        input.Nullability.ShouldBe(AutomationPortNullability.NonNullable);
        input.FixedValue.ShouldBe(new AutomationValue.Null(expectedType));
        transform.Binding(input.BindingFieldId).Mode.ShouldBe(AutomationInputBindingMode.Connected);
        var edge = f.Page.FindComponent<AutomationFlowCanvas>().Instance.Edges.Last();
        edge.SourceNodeId.ShouldBe(source.Id);
        edge.SourcePortId.Value.ShouldBe(sourcePort);
        edge.TargetPortId.ShouldBe(input.PortId);
        transform
            .Draft()
            .Definition.Configuration.GetProperty("inputs")
            .EnumerateArray()
            .Last()
            .GetProperty("fixed")
            .ValueKind.ShouldBe(JsonValueKind.Null);
        await f.Page.Instance.ApplyEditorHistoryShortcutAsync("undo");
        CurrentTransform(f.Page, transform.Id).TransformInputs.ShouldBe(originalInputs);
        f.Page.FindComponent<AutomationFlowCanvas>().Instance.Edges.ShouldBe(originalEdges);
        await f.Page.Instance.ApplyEditorHistoryShortcutAsync("redo");
        transform = CurrentTransform(f.Page, transform.Id);
        transform.TransformInputs.Last().ShouldBe(input);
        f.Page.FindComponent<AutomationFlowCanvas>()
            .Instance.Edges.Single(candidate => candidate.Id == edge.Id)
            .ShouldBe(edge);
        await DiscloseAsync(f.Page, transform.Id);
        inspector = f.Page.FindComponent<AutomationNodeInspector>();
        CelNameField(inspector, input.PortId).Change("renamed_input");
        CelNameField(inspector, input.PortId)
            .Closest(".automation-transform-declaration")!
            .QuerySelector("input")!
            .Input("Renamed bound input");
        var renamed = transform.TransformInputs.Last();
        renamed.PortId.ShouldBe(input.PortId);
        renamed.BindingFieldId.ShouldBe(input.BindingFieldId);
        renamed.FixedValue.ShouldBe(new AutomationValue.Null(expectedType));
        f.Page.Find("[data-automation-save-flow]").Click();
        f.Page.WaitForAssertion(() =>
            f.Page.FindComponent<AutomationEditorHeader>().Instance.HasChanges.ShouldBeFalse()
        );
        var rail = f.Page.FindComponent<AutomationFlowRail>();
        await f.Page.InvokeAsync(() =>
            rail.Instance.Select.InvokeAsync(rail.Instance.Flows.Single())
        );
        var reopened = CurrentTransform(f.Page, transform.Id);
        reopened.TransformInputs.Last().ShouldBe(renamed);
        reopened.Binding(input.BindingFieldId).Mode.ShouldBe(AutomationInputBindingMode.Connected);
        f.Page.FindComponent<AutomationFlowCanvas>()
            .Instance.Edges.Single(candidate => candidate.Id == edge.Id)
            .ShouldBe(edge);
        if (expectedType == AutomationPortValueType.Arguments)
        {
            await f.Page.InvokeAsync(
                f.Page.FindComponent<AutomationFlowRail>().Instance.Duplicate.InvokeAsync
            );
            var copied = f
                .Page.FindComponent<AutomationFlowCanvas>()
                .Instance.Nodes.Single(node =>
                    node.Definition.Id == AutomationDefinitionIds.CelTransform
                );
            copied.Id.ShouldNotBe(transform.Id);
            copied.TransformInputs.Last().ShouldBe(renamed);
            f.Page.FindComponent<AutomationFlowCanvas>()
                .Instance.Edges.ShouldContain(candidate =>
                    candidate.TargetNodeId == copied.Id
                    && candidate.TargetPortId == input.PortId
                    && candidate.SourcePortId.Value == sourcePort
                );
        }
        await using var db = await f.Database.CreateDbContextAsync();
        var saved = await db.AutomationFlowNodes.SingleAsync(node => node.Id == transform.Id.Value);
        JsonDocument
            .Parse(saved.ConfigurationJson)
            .RootElement.GetProperty("inputs")
            .EnumerateArray()
            .Last()
            .GetProperty("fixed")
            .ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public async Task SourceInputs_InvalidNamesCancelAndStaleSourcesLeaveGraphAndHistoryUntouched()
    {
        await using var f = await AutomationEditorPageFixture.CreateAsync(typedCommand: true);
        var transform = await AddSourceInputTransform(f.Page);
        var source = f
            .Page.FindComponent<AutomationFlowCanvas>()
            .Instance.Nodes.Single(node =>
                node.Definition.Id == AutomationDefinitionIds.CustomCommandSource
            );
        var inspector = f.Page.FindComponent<AutomationNodeInspector>();
        var before = transform.Draft();
        foreach (
            var rejected in source.Definition.Outputs.Where(port =>
                port.ValueType == AutomationPortValueType.Flow
                || port.Sensitivity == AutomationDataSensitivity.Sensitive
            )
        )
        {
            await f.Page.InvokeAsync(() =>
                inspector.Instance.AddInputFromSource.InvokeAsync(
                    new(source.Id, rejected, transform.Id, "Rejected", "rejected")
                )
            );
        }
        await f.Page.InvokeAsync(() =>
            inspector.Instance.AddInputFromSource.InvokeAsync(
                new(
                    transform.Id,
                    transform.Definition.Outputs.Single(),
                    transform.Id,
                    "Self",
                    "self"
                )
            )
        );
        transform.TransformInputs.Count.ShouldBe(1);
        var edges = inspector.Instance.Edges.ToArray();
        inspector.Find("button[aria-label='Add input from source']").Click();
        foreach (var invalid in new[] { "value", "1name", "arguments" })
        {
            inspector.Find("#automation-new-input-identifier").Input(invalid);
            inspector.Find("[data-automation-source-picker-complete]").Click();
            JsonElement
                .DeepEquals(
                    transform.Draft().Definition.Configuration,
                    before.Definition.Configuration
                )
                .ShouldBeTrue();
            inspector.Instance.Edges.ShouldBe(edges);
        }
        inspector.Find("[role=dialog]").KeyDown(new KeyboardEventArgs { Key = "Escape" });
        var port = source.Definition.Outputs.Single(port => port.Id.Value == "argument.seconds");
        var request = new AutomationTransformInputSourceRequest(
            source.Id,
            port,
            transform.Id,
            "Seconds",
            "seconds"
        );
        source.RefreshCommandDefinition(
            source.Definition with
            {
                Outputs = source
                    .Definition.Outputs.Where(value => value.Id != port.Id)
                    .ToImmutableArray(),
            }
        );
        await f.Page.InvokeAsync(() => inspector.Instance.AddInputFromSource.InvokeAsync(request));
        transform.TransformInputs.Count.ShouldBe(1);
        f.Page.FindComponent<AutomationFlowCanvas>().Instance.Edges.ShouldBe(edges);
        await f.Page.Instance.ApplyEditorHistoryShortcutAsync("undo");
        f.Page.FindComponent<AutomationFlowCanvas>()
            .Instance.Nodes.ShouldNotContain(node => node.Id == transform.Id);
    }

    [Test]
    public async Task SourceInputs_CurrentIdentityModeAndExplicitFixedEditsSurviveSourceChanges()
    {
        await using var f = await AutomationEditorPageFixture.CreateAsync(typedCommand: true);
        var transform = await AddSourceInputTransform(f.Page);
        var source = f
            .Page.FindComponent<AutomationFlowCanvas>()
            .Instance.Nodes.Single(node =>
                node.Definition.Id == AutomationDefinitionIds.CustomCommandSource
            );
        var inspector = f.Page.FindComponent<AutomationNodeInspector>();
        inspector.Find("button[aria-label='Add input from source']").Click();
        ChooseSourceRadio(
                inspector,
                source,
                source.Definition.Outputs.Single(port => port.Id.Value == "argument.name")
            )
            .Click();
        inspector.Find("[data-automation-source-picker-complete]").Click();
        var input = transform.TransformInputs.Last();
        var edge = inspector.Instance.Edges.Last();
        var section = inspector.Find($"[data-automation-field='{input.BindingFieldId.Value}']");
        section.QuerySelector("[data-automation-source-picker-open]")!.Click();
        ChooseSourceRadio(
                inspector,
                source,
                source.Definition.Outputs.Single(port => port.Id.Value == "argument.name")
            )
            .GetAttribute("aria-checked")
            .ShouldBe("true");
        inspector.Find("[role=dialog]").KeyDown(new KeyboardEventArgs { Key = "Escape" });
        foreach (var fixedText in new[] { string.Empty, "kept literal" })
        {
            section = inspector.Find($"[data-automation-field='{input.BindingFieldId.Value}']");
            section
                .QuerySelectorAll(".automation-binding-mode-tabs button")
                .Single(button => button.TextContent == "Fixed")
                .Click();
            inspector
                .Find($"[data-automation-field='{input.BindingFieldId.Value}'] input")
                .Input(fixedText);
            transform
                .TransformInputs.Last()
                .FixedValue.ShouldBe(new AutomationValue.Text(fixedText));
            inspector
                .Find($"[data-automation-field='{input.BindingFieldId.Value}']")
                .QuerySelectorAll(".automation-binding-mode-tabs button")
                .Single(button => button.TextContent == "Expression")
                .Click();
            inspector
                .Find($"[data-automation-field='{input.BindingFieldId.Value}'] textarea")
                .Input("\"retained expression\"");
            inspector
                .Find(
                    $"[data-automation-field='{input.BindingFieldId.Value}'] [data-automation-source-picker-open]"
                )
                .Click();
            inspector.Find("[role=dialog]").KeyDown(new KeyboardEventArgs { Key = "Escape" });
            transform
                .Binding(input.BindingFieldId)
                .Mode.ShouldBe(AutomationInputBindingMode.Expression);
            inspector
                .Find(
                    $"[data-automation-field='{input.BindingFieldId.Value}'] [data-automation-source-picker-open]"
                )
                .Click();
            inspector.Find("[data-automation-source-picker-complete]").Click();
            transform
                .Binding(input.BindingFieldId)
                .Mode.ShouldBe(AutomationInputBindingMode.Connected);
            transform
                .Binding(input.BindingFieldId)
                .Expression!.Source.ShouldBe("\"retained expression\"");
            inspector
                .Instance.Edges.Single(candidate =>
                    candidate.TargetNodeId == transform.Id && candidate.TargetPortId == input.PortId
                )
                .ShouldBe(edge);
            CelNameField(inspector, input.PortId)
                .Closest(".automation-transform-declaration")!
                .QuerySelector("input")!
                .Input("Renamed text input");
            var restored = AutomationEditorNode.Restore(transform.Draft(), transform.Definition);
            restored
                .TransformInputs.Last()
                .FixedValue.ShouldBe(new AutomationValue.Text(fixedText));
            restored
                .Binding(input.BindingFieldId)
                .ShouldBe(transform.Binding(input.BindingFieldId));
            f.Page.Find("[data-automation-save-flow]").Click();
            f.Page.WaitForAssertion(() =>
                f.Page.FindComponent<AutomationEditorHeader>().Instance.HasChanges.ShouldBeFalse()
            );
            var rail = f.Page.FindComponent<AutomationFlowRail>();
            await f.Page.InvokeAsync(() =>
                rail.Instance.Select.InvokeAsync(rail.Instance.Flows.Single())
            );
            transform = CurrentTransform(f.Page, transform.Id);
            transform
                .TransformInputs.Last()
                .FixedValue.ShouldBe(new AutomationValue.Text(fixedText));
            transform
                .Binding(input.BindingFieldId)
                .Expression!.Source.ShouldBe("\"retained expression\"");
            await DiscloseAsync(f.Page, transform.Id);
            inspector = f.Page.FindComponent<AutomationNodeInspector>();
        }
        inspector
            .Find($"[data-automation-field='{input.BindingFieldId.Value}']")
            .QuerySelectorAll(".automation-binding-mode-tabs button")
            .Single(button => button.TextContent == "Expression")
            .Click();
        await f.Page.InvokeAsync(
            f.Page.FindComponent<AutomationEditorHeader>().Instance.Validate.InvokeAsync
        );
        inspector.Instance.Errors.ShouldContain(error =>
            error.NodeId == transform.Id
            && error.FieldId == input.BindingFieldId
            && error.Code == "binding-connection-inactive"
        );
        transform
            .Binding(input.BindingFieldId)
            .Mode.ShouldBe(AutomationInputBindingMode.Expression);
        transform
            .Binding(input.BindingFieldId)
            .Expression!.Source.ShouldBe("\"retained expression\"");
    }

    [Test]
    public async Task SourceInputs_NullableInheritanceAndDuplicateLabelsKeepExactCurrentSourceAndRepairIdentity()
    {
        await using var f = await AutomationEditorPageFixture.CreateAsync(typedCommand: true);
        var first = await AddSourceInputTransform(f.Page);
        var second = await AddSourceInputTransform(f.Page);
        foreach (var producer in new[] { first, second })
        {
            producer.SetDisplayAlias("Values");
            var produced = producer.TransformOutputs.Single();
            producer.UpdateTransformOutput(
                produced.PortId,
                produced.DisplayName,
                AutomationPortValueType.Number,
                AutomationPortNullability.Nullable,
                "null"
            );
        }
        var target = await AddSourceInputTransform(f.Page);
        var inspector = f.Page.FindComponent<AutomationNodeInspector>();
        inspector.Find("button[aria-label='Add input from source']").Click();
        inspector
            .FindAll("[role=radio]")
            .Where(button =>
                button.QuerySelector("strong")!.TextContent
                == $"{second.EffectiveName} · {second.Definition.Outputs.Single().Name}"
            )
            .Last()
            .Click();
        inspector.Find("[data-automation-source-picker-complete]").Click();
        var input = target.TransformInputs.Last();
        input.ValueType.ShouldBe(AutomationPortValueType.Number);
        input.Nullability.ShouldBe(AutomationPortNullability.Nullable);
        input.FixedValue.ShouldBe(new AutomationValue.Null(AutomationPortValueType.Number));
        var edge = inspector.Instance.Edges.Last();
        edge.SourceNodeId.ShouldBe(second.Id);
        var opener =
            $"[data-automation-field='{input.BindingFieldId.Value}'] [data-automation-source-picker-open]";
        inspector.Find(opener).Click();
        inspector.Find("[data-automation-source-picker-complete]").Click();
        inspector.Instance.Edges.Last().ShouldBe(edge);
        var required = CelNameField(inspector, input.PortId)
            .Closest(".automation-transform-declaration")!
            .QuerySelector("select[id$='required']")!;
        required.Change(AutomationPortNullability.NonNullable.ToString());
        _ = AutomationConnections.Issue(edge, inspector.Instance.Nodes).ShouldNotBeNull();
        target
            .TransformInputs.Last()
            .FixedValue.ShouldBe(new AutomationValue.Null(AutomationPortValueType.Number));
        inspector.Find(opener).Click();
        inspector
            .Find("[data-automation-source-picker-complete]")
            .HasAttribute("disabled")
            .ShouldBeTrue();
        inspector.Find("[role=dialog]").KeyDown(new KeyboardEventArgs { Key = "Escape" });
        var output = first.TransformOutputs.Single();
        first.UpdateTransformOutput(
            output.PortId,
            output.DisplayName,
            AutomationPortValueType.Number,
            AutomationPortNullability.NonNullable,
            "2"
        );
        await f.Page.InvokeAsync(inspector.Instance.Changed.InvokeAsync);
        inspector.Find(opener).Click();
        inspector
            .FindAll("[role=radio]")
            .Where(button =>
                button.QuerySelector("strong")!.TextContent
                == $"{first.EffectiveName} · {first.Definition.Outputs.Single().Name}"
            )
            .First()
            .Click();
        inspector.Find("[data-automation-source-picker-complete]").Click();
        var repaired = inspector.Instance.Edges.Last();
        repaired.Id.ShouldBe(edge.Id);
        repaired.SourceNodeId.ShouldBe(first.Id);
        AutomationConnections.Issue(repaired, inspector.Instance.Nodes).ShouldBeNull();
        first.RemoveTransformOutput(output.PortId);
        await f.Page.InvokeAsync(inspector.Instance.Changed.InvokeAsync);
        inspector.Find(opener).Click();
        inspector
            .Find("[data-automation-source-picker-complete]")
            .HasAttribute("disabled")
            .ShouldBeTrue();
        var command = inspector.Instance.Nodes.Single(node =>
            node.Definition.Id == AutomationDefinitionIds.CustomCommandSource
        );
        ChooseSourceRadio(
                inspector,
                command,
                command.Definition.Outputs.Single(port => port.Id.Value == "argument.seconds")
            )
            .Click();
        inspector.Find("[data-automation-source-picker-complete]").Click();
        var replacement = inspector.Instance.Edges.Single(candidate => candidate.Id == edge.Id);
        replacement.SourceNodeId.ShouldBe(command.Id);
        replacement.SourcePortId.Value.ShouldBe("argument.seconds");
        target.TransformInputs.Last().PortId.ShouldBe(input.PortId);
        target.TransformInputs.Last().BindingFieldId.ShouldBe(input.BindingFieldId);
    }

    private static async Task<AutomationEditorNode> AddSourceInputTransform(
        IRenderedComponent<AutomationEditorPage> page
    )
    {
        var existing = page.FindComponent<AutomationFlowCanvas>()
            .Instance.Nodes.Select(node => node.Id)
            .ToHashSet();
        page.Find("[data-automation-toolbox-toggle]").Click();
        var definition = page.FindComponent<AutomationToolbox>()
            .Instance.Definitions.Single(item => item.Id == AutomationDefinitionIds.CelTransform);
        page.Find("[data-automation-toolbox-search]").Input(definition.Display.Name);
        var row = page.FindComponents<EditorToolboxRow>()
            .Single(item =>
                item.FindComponent<AutomationNodeIcon>().Instance.DefinitionId == definition.Id
            );
        await page.InvokeAsync(row.Instance.Activate.InvokeAsync);
        var transform = page.FindComponent<AutomationFlowCanvas>()
            .Instance.Nodes.Single(node =>
                node.Definition.Id == definition.Id && !existing.Contains(node.Id)
            );
        await DiscloseAsync(page, transform.Id);
        return transform;
    }

    private static AutomationEditorNode CurrentTransform(
        IRenderedComponent<AutomationEditorPage> page,
        AutomationNodeId id
    ) => page.FindComponent<AutomationFlowCanvas>().Instance.Nodes.Single(node => node.Id == id);

    private static AngleSharp.Dom.IElement ChooseSourceRadio(
        IRenderedComponent<AutomationNodeInspector> inspector,
        AutomationEditorNode source,
        AutomationPortMetadata port
    ) =>
        inspector
            .FindAll("[role=radio]")
            .Single(button =>
                button.QuerySelector("strong")!.TextContent
                == $"{source.EffectiveName} · {port.Name}"
            );
}
