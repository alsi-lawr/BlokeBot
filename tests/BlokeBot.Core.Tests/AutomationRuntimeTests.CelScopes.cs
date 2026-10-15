using System.Collections.Immutable;
using System.Text.Json;
using AngleSharp.Dom;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.Automations.Page;
using BlokeBot.Persistence.Models;
using Bunit;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class AutomationRuntimeTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CelScopes_InspectorExamplesSaveReloadAndDriveConditionAndDelay(
        bool enoughArguments
    )
    {
        await using var f = await RuntimeFixture.CreateAsync(migrateSchema: true);
        using var ui = new BunitContext();
        ui.JSInterop.Mode = JSRuntimeMode.Loose;
        var condition = ScopeNode(AutomationDefinitionIds.ConditionControl);
        var delay = ScopeNode(AutomationDefinitionIds.DelayControl);
        var predicateUi = ScopeInspector(ui, condition);
        var durationUi = ScopeInspector(ui, delay);
        var predicate = ScopeInputExample(predicateUi, condition);
        var duration = ScopeInputExample(durationUi, delay);
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var finish = Node("send-chat", """{"message":"finished"}""");
        var rejected = Node("send-chat", """{"message":"not-ready"}""");
        var conditionDraft = condition.Draft();
        var delayDraft = delay.Draft();
        var draft = Draft(
            f.HostId,
            [source, conditionDraft, delayDraft, finish, rejected],
            [
                Edge(source, "flow", conditionDraft),
                Edge(conditionDraft, "yes", delayDraft),
                Edge(conditionDraft, "no", rejected),
                Edge(delayDraft, "complete", finish),
            ]
        );
        var saved = (
            await f.Flows.SaveAsync(draft, CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        var loaded = (await f.Flows.ListAsync(new(f.HostId), CancellationToken.None))
            .ShouldBeOfType<AutomationFlowQueryOutcome.Available>()
            .Flows.Single()
            .Draft;
        loaded.Id.ShouldBe(saved.FlowId);
        var restoredCondition = AutomationEditorNode.Restore(
            loaded.Nodes.Single(node => node.Id == condition.Id),
            condition.Definition
        );
        var restoredDelay = AutomationEditorNode.Restore(
            loaded.Nodes.Single(node => node.Id == delay.Id),
            delay.Definition
        );
        restoredCondition.Binding(new("predicate")).Expression!.Source.ShouldBe(predicate);
        restoredDelay
            .Binding(AutomationDelayDurationBinding.ValueField)
            .Expression!.Source.ShouldBe(duration);
        var now = f.Clock.GetUtcNow();
        var context = CelTimerContext(
            f.HostId,
            enoughArguments ? ["0.002", "private-timer-name"] : ["0.002"]
        );
        var run = (
            await f.NewRuntime()
                .DispatchAsync(
                    new(context, new CustomCommandSourceConfiguration(new(7))),
                    CancellationToken.None
                )
        ).RunIds.ShouldHaveSingleItem();
        var trace = await ReadTraceAsync(f, new(run.Value));
        trace.Events.ShouldContain(item =>
            item.Event.Kind == AutomationTraceEventKind.Branch
            && item.Event.Node!.Id == condition.Id
            && item.Event.SelectedPort == new AutomationPortId(enoughArguments ? "yes" : "no")
        );
        if (enoughArguments)
        {
            f.Chat.Messages.ShouldBeEmpty();
            await using (var db = await f.Database.CreateDbContextAsync())
            {
                (await db.AutomationFlowRuns.SingleAsync()).Status.ShouldBe(
                    AutomationFlowRunStatus.Waiting
                );
                (
                    await db.AutomationNodeRuns.SingleAsync(node => node.NodeId == finish.Id.Value)
                ).AvailableAtUtc.ShouldBe(now.UtcDateTime.AddMilliseconds(2));
            }
            f.Clock.Advance(TimeSpan.FromTicks((2 * TimeSpan.TicksPerMillisecond) - 1));
            (await f.NewRuntime().ResumeAsync(run, CancellationToken.None)).Status.ShouldBe(
                AutomationResumeStatus.Waiting
            );
            f.Chat.Messages.ShouldBeEmpty();
            f.Clock.Advance(TimeSpan.FromTicks(1));
            (await f.NewRuntime().ResumeAsync(run, CancellationToken.None)).Status.ShouldBe(
                AutomationResumeStatus.Completed
            );
            (await f.NewRuntime().ResumeAsync(run, CancellationToken.None)).Status.ShouldBe(
                AutomationResumeStatus.Completed
            );
            f.Chat.Messages.ShouldBe(["finished"]);
        }
        else
        {
            f.Chat.Messages.ShouldBe(["not-ready"]);
            await using var db = await f.Database.CreateDbContextAsync();
            (await db.AutomationFlowRuns.SingleAsync()).Status.ShouldBe(
                AutomationFlowRunStatus.Completed
            );
            (
                await db.AutomationNodeRuns.AnyAsync(node => node.NodeId == delay.Id.Value)
            ).ShouldBeFalse();
        }
        JsonSerializer.Serialize(trace).ShouldNotContain("private-timer-name");
    }

    [Test]
    public async Task CelScopes_SafeBindingAndRenamedOutputExamplesRepairActualFieldErrorsAndExecute()
    {
        await using var f = await RuntimeFixture.CreateAsync(migrateSchema: true);
        using var ui = new BunitContext();
        ui.JSInterop.Mode = JSRuntimeMode.Loose;
        var transform = ScopeNode(AutomationDefinitionIds.CelTransform);
        var inspector = ScopeInspector(ui, transform);
        var inputId = transform.TransformInputs.Single().PortId;
        ScopeDeclaration(inspector, inputId, "type")
            .Change(AutomationPortValueType.Arguments.ToString());
        ScopeDeclaration(inspector, inputId, "cel-name").Change("join");
        var safeSource = ScopeInputExample(inspector, transform);
        var amountId = transform.TransformOutputs.Single().PortId;
        ScopeDeclaration(inspector, amountId, "type")
            .Change(AutomationPortValueType.Number.ToString());
        var amountSource = ScopeOutputExample(inspector, transform, amountId);
        inspector.Find("button[aria-label='Add output']").Click();
        var nameId = transform.TransformOutputs.Last().PortId;
        var nameSource = ScopeOutputExample(inspector, transform, nameId);
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var send = Node(
            "send-chat",
            """{"message":"unused"}""",
            bindings: Bindings("message", AutomationInputBindingMode.Connected)
        );
        var delay = Node(
            "delay",
            """{"duration-milliseconds":1}""",
            bindings: Bindings(
                AutomationDelayDurationBinding.ValueField.Value,
                AutomationInputBindingMode.Connected
            )
        );
        var finish = Node("send-chat", """{"message":"done"}""");
        AutomationFlowDraft Graph() =>
            Draft(
                f.HostId,
                [source, transform.Draft(), send, delay, finish],
                [
                    Edge(source, "flow", send),
                    Edge(send, "complete", delay),
                    Edge(delay, "complete", finish),
                    Edge(transform.Draft(), nameId.Value, send, "message", AutomationEdgeKind.Data),
                    Edge(
                        transform.Draft(),
                        amountId.Value,
                        delay,
                        "duration",
                        AutomationEdgeKind.Data
                    ),
                ]
            );
        (await f.Flows.ValidateAsync(Graph(), CancellationToken.None)).Errors.ShouldBeEmpty();
        var input = transform.TransformInputs.Single();
        var bindingField = inspector.Find(
            $"[data-automation-field='{input.BindingFieldId.Value}'] textarea"
        );
        foreach (var invalid in new[] { "private_value", "size(arguments)" })
        {
            bindingField.Input(invalid);
            var errors = (await f.Flows.ValidateAsync(Graph(), CancellationToken.None)).Errors;
            var error = errors.Single(value =>
                value.NodeId == transform.Id && value.FieldId == input.BindingFieldId
            );
            error
                .Diagnostic.ShouldNotBeNull()
                .Category.ShouldBe(
                    invalid == "private_value"
                        ? AutomationDiagnosticCategory.UnavailableName
                        : AutomationDiagnosticCategory.IncompatibleType
                );
            inspector.Render(parameters => parameters.Add(component => component.Errors, errors));
            inspector
                .Find($"[data-automation-field='{input.BindingFieldId.Value}'] textarea")
                .GetAttribute("aria-invalid")
                .ShouldBe("true");
            inspector
                .Find($"[data-automation-field='{input.BindingFieldId.Value}'] [role=alert]")
                .TextContent.ShouldNotContain("private_value");
            bindingField.Input(safeSource);
        }
        foreach (
            var unavailable in new[]
            {
                "arguments",
                "private_value",
                transform.TransformOutputs.First().DisplayName,
            }
        )
        {
            ScopeDeclaration(inspector, nameId, "expression").Input(unavailable);
            var errors = (await f.Flows.ValidateAsync(Graph(), CancellationToken.None)).Errors;
            errors
                .Single(error => error.NodeId == transform.Id && error.PortId == nameId)
                .Diagnostic.ShouldNotBeNull()
                .Category.ShouldBe(AutomationDiagnosticCategory.UnavailableName);
            inspector.Render(parameters => parameters.Add(component => component.Errors, errors));
            ScopeDeclaration(inspector, nameId, "expression")
                .GetAttribute("aria-invalid")
                .ShouldBe("true");
            ScopeDeclaration(inspector, nameId, "expression").Input(nameSource);
        }
        var repaired = await f.Flows.ValidateAsync(Graph(), CancellationToken.None);
        repaired.Errors.ShouldBeEmpty();
        inspector.Render(parameters =>
            parameters.Add(component => component.Errors, repaired.Errors)
        );
        ScopeDeclaration(inspector, nameId, "expression")
            .GetAttribute("aria-invalid")
            .ShouldBeNull();
        _ = (
            await f.Flows.SaveAsync(Graph(), CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        var loaded = (await f.Flows.ListAsync(new(f.HostId), CancellationToken.None))
            .ShouldBeOfType<AutomationFlowQueryOutcome.Available>()
            .Flows.Single()
            .Draft;
        var restored = AutomationEditorNode.Restore(
            loaded.Nodes.Single(node => node.Id == transform.Id),
            transform.Definition
        );
        restored.TransformInputs.Single().Identifier.Value.ShouldBe("join");
        restored
            .TransformOutputs.Single(output => output.PortId == amountId)
            .Source.ShouldBe(amountSource);
        restored
            .TransformOutputs.Single(output => output.PortId == nameId)
            .Source.ShouldBe(nameSource);
        var run = (
            await f.NewRuntime()
                .DispatchAsync(
                    new(
                        CelTimerContext(f.HostId, ["2", "one", "two"]),
                        new CustomCommandSourceConfiguration(new(7))
                    ),
                    CancellationToken.None
                )
        ).RunIds.ShouldHaveSingleItem();
        f.Chat.Messages.ShouldBe(["one two"]);
        f.Clock.Advance(TimeSpan.FromMilliseconds(2));
        (await f.NewRuntime().ResumeAsync(run, CancellationToken.None)).Status.ShouldBe(
            AutomationResumeStatus.Completed
        );
        f.Chat.Messages.ShouldBe(["one two", "done"]);
        JsonSerializer
            .Serialize(await ReadTraceAsync(f, new(run.Value)))
            .ShouldNotContain("one two");
    }

    [Test]
    public void CelScopes_CurrentDeclarationTypeNullabilityAndRemovalControlUsableOutputExamples()
    {
        using var ui = new BunitContext();
        ui.JSInterop.Mode = JSRuntimeMode.Loose;
        var node = ScopeNode(AutomationDefinitionIds.CelTransform);
        var inspector = ScopeInspector(ui, node);
        var inputId = node.TransformInputs.Single().PortId;
        var outputId = node.TransformOutputs.Single().PortId;
        ScopeDeclaration(inspector, outputId, "type")
            .Change(AutomationPortValueType.Number.ToString());
        inspector.Find(".automation-input-editor input[type=text]").Input("2.5");
        _ = ScopeOutputExample(inspector, node, outputId);
        ScopeAssertFixedOutput(node, new AutomationValue.Number(2.5m));
        ScopeDeclaration(inspector, inputId, "cel-name").Change("amount");
        _ = ScopeOutputExample(inspector, node, outputId);
        ScopeAssertFixedOutput(node, new AutomationValue.Number(2.5m));
        ScopeDeclaration(inspector, inputId, "type")
            .Change(AutomationPortValueType.Number.ToString());
        _ = ScopeOutputExample(inspector, node, outputId);
        ScopeAssertFixedOutput(node, new AutomationValue.Number(2.5m));
        ScopeDeclaration(inspector, inputId, "required")
            .Change(AutomationPortNullability.Nullable.ToString());
        var output = node.TransformOutputs.Single();
        AutomationTransformCelService
            .ValidateOutput(
                output,
                node.TransformInputs.ToDictionary(value => value.Identifier.Value),
                out var nullableDiagnostic
            )
            .ShouldBeFalse();
        nullableDiagnostic
            .ShouldNotBeNull()
            .Category.ShouldBe(AutomationDiagnosticCategory.IncompatibleType);
        ScopeDeclaration(inspector, outputId, "required")
            .Change(AutomationPortNullability.Nullable.ToString());
        inspector.Find(".automation-input-editor input[type=number]").Input("");
        _ = ScopeOutputExample(inspector, node, outputId);
        ScopeAssertFixedOutput(node, new AutomationValue.Null(AutomationPortValueType.Number));
        inspector
            .Find($"button[aria-label='Remove input {node.TransformInputs.Single().DisplayName}']")
            .Click();
        AutomationTransformCelService
            .ValidateOutput(
                node.TransformOutputs.Single(),
                node.TransformInputs.ToDictionary(value => value.Identifier.Value),
                out var removedDiagnostic
            )
            .ShouldBeFalse();
        removedDiagnostic
            .ShouldNotBeNull()
            .Category.ShouldBe(AutomationDiagnosticCategory.UnavailableName);
        inspector.Find("button[aria-label='Declare input manually']").Click();
        inspector.Find(".automation-input-editor input[type=text]").Input("4.25");
        _ = ScopeOutputExample(inspector, node, outputId);
        ScopeAssertFixedOutput(node, new AutomationValue.Number(4.25m));
    }

    [Test]
    public void CelScopes_DeclaredStreamFieldExampleRequiresNullableDestinationAndEvaluatesAbsentValue()
    {
        using var ui = new BunitContext();
        ui.JSInterop.Mode = JSRuntimeMode.Loose;
        var node = ScopeNode(AutomationDefinitionIds.CelTransform);
        var inspector = ScopeInspector(ui, node);
        var inputId = node.TransformInputs.Single().PortId;
        var outputId = node.TransformOutputs.Single().PortId;
        ScopeDeclaration(inspector, inputId, "type")
            .Change(AutomationPortValueType.Stream.ToString());
        var field = AutomationCelCompletions
            .ForOutput(node)
            .First(name =>
                name.Type.ValueType == AutomationPortValueType.Text && name.Type.CanBeNull
            );
        ScopeDeclaration(inspector, outputId, "expression").Input(field.Name);
        AutomationTransformCelService
            .ValidateOutput(
                node.TransformOutputs.Single(),
                node.TransformInputs.ToDictionary(input => input.Identifier.Value),
                out var requiredDiagnostic
            )
            .ShouldBeFalse();
        requiredDiagnostic
            .ShouldNotBeNull()
            .Category.ShouldBe(AutomationDiagnosticCategory.IncompatibleType);
        ScopeDeclaration(inspector, outputId, "required")
            .Change(AutomationPortNullability.Nullable.ToString());
        var source = inspector
            .FindAll(
                $"[data-automation-output='{outputId.Value}'] .automation-cel-completions code"
            )
            .First(code => code.TextContent == field.Name)
            .TextContent;
        ScopeDeclaration(inspector, outputId, "expression").Input(source);
        var configuration = new AutomationCelTransformConfiguration(
            [.. node.TransformInputs],
            [.. node.TransformOutputs]
        );
        AutomationTransformCelService
            .ValidateOutput(
                configuration.Outputs.Single(),
                configuration.Inputs.ToDictionary(input => input.Identifier.Value),
                out var diagnostic
            )
            .ShouldBeTrue();
        diagnostic.ShouldBeNull();
        var inputs = ImmutableDictionary<AutomationPortId, AutomationResolvedValue>.Empty.Add(
            inputId,
            new(
                new AutomationValue.Stream(new(null, null, null)),
                [AutomationValueProvenance.Generated]
            )
        );
        new AutomationTransformCelService()
            .Execute(configuration, inputs)
            .ShouldBeOfType<AutomationPureNodeResult.Succeeded>()
            .Outputs[outputId]
            .Value.ShouldBe(new AutomationValue.Null(AutomationPortValueType.Text));
    }

    [Test]
    public void CelScopes_FormattingRequiresDecimalAndIntegerPrecisionInSafeScopeNotOrdinary()
    {
        var source = LanguageExpression("format_number(decimal(arguments[0]), 2)");
        var context = LanguageContext(["2.5"]);
        var port = LanguagePort(AutomationPortValueType.Text);
        var safe = new AutomationSafeTriggerExpressionService();
        safe.Validate(source, port, out _).ShouldBeTrue();
        safe.Evaluate(source, port, context)
            .ShouldNotBeNull()
            .Value.ShouldBe(new AutomationValue.Text("2.50"));
        safe.Validate(LanguageExpression("format_number(size(arguments), 2)"), port, out _)
            .ShouldBeFalse();
        safe.Validate(LanguageExpression("format_number(decimal(arguments[0]), 2.0)"), port, out _)
            .ShouldBeFalse();
        _ = new AutomationExpressionService()
            .Evaluate(source, context)
            .ShouldBeOfType<AutomationExpressionResult.Invalid>();
    }

    private static AutomationEditorNode ScopeNode(AutomationDefinitionId definition) =>
        AutomationEditorNode.Create(
            new CoreAutomationCatalogModule()
                .Definitions.Single(item => item.Descriptor.Id == definition)
                .Descriptor,
            default
        );

    private static IRenderedComponent<AutomationNodeInspector> ScopeInspector(
        BunitContext ui,
        AutomationEditorNode node
    ) =>
        ui.Render<AutomationNodeInspector>(parameters =>
            parameters
                .Add(component => component.Node, node)
                .Add(component => component.Nodes, [node])
        );

    private static IElement ScopeDeclaration(
        IRenderedComponent<AutomationNodeInspector> inspector,
        AutomationPortId id,
        string suffix
    ) => inspector.Find($"[id='automation-declaration-{id.Value}-{suffix}']");

    private static string ScopeInputExample(
        IRenderedComponent<AutomationNodeInspector> inspector,
        AutomationEditorNode node
    )
    {
        var input = node.Definition.Inputs.Single(port => port.BindingFieldId is not null);
        var section = inspector.Find(
            $"[data-automation-field='{input.BindingFieldId!.Value.Value}']"
        );
        section
            .QuerySelectorAll(".automation-binding-mode-tabs button")
            .Single(button =>
                button.TextContent == AutomationInputBindingMode.Expression.ToString()
            )
            .Click();
        var offered = AutomationCelCompletions.InputExamples(node, input).First();
        var actual = inspector
            .FindAll(".automation-cel-completions code")
            .First(code => code.TextContent == offered)
            .TextContent;
        inspector
            .Find($"[data-automation-field='{input.BindingFieldId.Value.Value}'] textarea")
            .Input(actual);
        return actual;
    }

    private static string ScopeOutputExample(
        IRenderedComponent<AutomationNodeInspector> inspector,
        AutomationEditorNode node,
        AutomationPortId outputId
    )
    {
        var output = node.TransformOutputs.Single(value => value.PortId == outputId);
        var offered = AutomationCelCompletions.OutputExamples(node, output).First();
        var actual = inspector
            .Find($"[data-automation-output='{outputId.Value}'] .automation-cel-completions code")
            .TextContent;
        actual.ShouldBe(offered);
        ScopeDeclaration(inspector, outputId, "expression").Input(actual);
        return actual;
    }

    private static void ScopeAssertFixedOutput(AutomationEditorNode node, AutomationValue expected)
    {
        var restored = AutomationEditorNode.Restore(node.Draft(), node.Definition);
        var configuration = new AutomationCelTransformConfiguration(
            [.. restored.TransformInputs],
            [.. restored.TransformOutputs]
        );
        AutomationTransformCelService
            .ValidateOutput(
                configuration.Outputs.Single(),
                configuration.Inputs.ToDictionary(value => value.Identifier.Value),
                out var diagnostic
            )
            .ShouldBeTrue();
        diagnostic.ShouldBeNull();
        var inputs = configuration.Inputs.ToImmutableDictionary(
            value => value.PortId,
            value => new AutomationResolvedValue(
                value.FixedValue,
                [AutomationValueProvenance.Generated]
            )
        );
        new AutomationTransformCelService()
            .Execute(configuration, inputs)
            .ShouldBeOfType<AutomationPureNodeResult.Succeeded>()
            .Outputs.Single()
            .Value.Value.ShouldBe(expected);
    }
}
