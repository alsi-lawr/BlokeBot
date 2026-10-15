using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.Automations.Page;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class AutomationRuntimeTests
{
    [Test]
    public async Task SourceInputs_DeclaredArgumentsAndTypedNumberEvaluateThroughSavedRuntimeWithoutGlobals()
    {
        await using var f = await RuntimeFixture.CreateAsync();
        (await SaveArgumentSchema(f, CustomCommandArgumentTests.TimerSchema)).ShouldBeNull();
        var definitions = new CoreAutomationCatalogModule()
            .Definitions.Select(value => value.Descriptor)
            .ToArray();
        var editor = AutomationEditorState.Create("Source inputs");
        editor.IsEnabled = true;
        var source = editor.AddNode(
            AutomationCommandSource.Project(
                definitions.Single(value =>
                    value.Id == AutomationDefinitionIds.CustomCommandSource
                ),
                CustomCommandArgumentTests.TimerSchema
            )
        );
        source.SetValue(new("custom-command-id"), "7");
        var transform = editor.AddNode(
            definitions.Single(value => value.Id == AutomationDefinitionIds.CelTransform)
        );
        foreach (
            var (portId, identifier) in new[]
            {
                ("arguments", "args"),
                ("argument.seconds", "seconds"),
            }
        )
        {
            var output = source.Definition.Outputs.Single(port => port.Id.Value == portId);
            var input = transform
                .AddTransformInputFromSource(output, identifier, identifier)
                .ShouldNotBeNull();
            editor.Edges.Add(
                new(
                    Guid.NewGuid(),
                    AutomationEdgeKind.Data,
                    source.Id,
                    output.Id,
                    transform.Id,
                    input.PortId
                )
            );
        }
        var result = transform.TransformOutputs.Single();
        transform.UpdateTransformOutput(
            result.PortId,
            result.DisplayName,
            result.ValueType,
            result.Nullability,
            "seconds == decimal(\"1.25\") ? join(args, 0, size(args), \"|\") : \"wrong\""
        );
        var send = editor.AddNode(
            definitions.Single(value => value.Id == AutomationDefinitionIds.SendChatAction)
        );
        send.SetBindingMode(new("message"), AutomationInputBindingMode.Connected);
        editor.Edges.Add(
            new(
                Guid.NewGuid(),
                AutomationEdgeKind.Flow,
                source.Id,
                new("flow"),
                send.Id,
                new("flow")
            )
        );
        editor.Edges.Add(
            new(
                Guid.NewGuid(),
                AutomationEdgeKind.Data,
                transform.Id,
                result.PortId,
                send.Id,
                new("message")
            )
        );
        var saved = (
            await f.Flows.SaveAsync(editor.Draft(new(f.HostId)), default)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        var read = (
            await f.Flows.ReadForAuthoringAsync(new(f.HostId), saved.FlowId, default)
        ).ShouldBeOfType<AutomationFlowAuthoringReadOutcome.Editor>();
        read.Errors.ShouldBeEmpty();
        var restored = AutomationEditorNode.Restore(
            read.Snapshot.Draft.Nodes.Single(node => node.Id == transform.Id),
            transform.Definition
        );
        restored
            .TransformInputs.Skip(1)
            .Select(input => input.FixedValue)
            .ShouldBe([
                new AutomationValue.Null(AutomationPortValueType.Arguments),
                new AutomationValue.Null(AutomationPortValueType.Number),
            ]);
        var run = (
            await f.Runtime.DispatchAsync(
                new(
                    TypedCommandContext(f.HostId, "1.25 tea"),
                    new CustomCommandSourceConfiguration(new(7))
                ),
                default
            )
        ).RunIds.ShouldHaveSingleItem();
        f.Chat.Messages.ShouldBe(["1.25|tea"]);
        (await f.Runtime.ResumeAsync(run, default)).Status.ShouldBe(
            AutomationResumeStatus.Completed
        );
        f.Chat.Messages.ShouldBe(["1.25|tea"]);
        transform.UpdateTransformOutput(
            result.PortId,
            result.DisplayName,
            result.ValueType,
            result.Nullability,
            "arguments[0]"
        );
        (
            await f.Flows.ValidateAsync(editor.Draft(new(f.HostId)), default)
        ).Errors.ShouldNotBeEmpty();
    }
}
