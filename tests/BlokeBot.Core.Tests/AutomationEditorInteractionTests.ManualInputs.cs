using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.Automations.Page;
using Bunit;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class AutomationEditorInteractionTests
{
    [Test]
    public async Task AuthoringUi_ManualSourcesUseEditedTypesAndInvalidNumbersCannotRunOldValues()
    {
        var (editor, follow, cheer) = ManualInputEditor();
        await using var f = await AutomationEditorPageFixture.CreateAsync(authored: editor);
        var page = f.Page;
        page.Find("[data-automation-test-flow]").Click();
        await Select(follow.Id);
        SourceInput(AutomationPortValueType.Actor).FindAll("input")[1].Change("Authored viewer");
        await RunAndExpect("follow-selected");
        await Select(cheer.Id);
        var panel = page.FindComponent<AutomationScenarioPanel>();
        panel.Instance.Editor!.Fixture.Context.Actor!.DisplayName.ShouldNotBe("Authored viewer");
        page.FindAll("[data-automation-trace]").ShouldBeEmpty();
        var number = SourceInput(AutomationPortValueType.Number);
        number.Find("input").Change("42");
        SourceInput(AutomationPortValueType.Boolean).Find("select").Change("True");
        SourceInput(AutomationPortValueType.Actor).Find("input[type=checkbox]").Change(true);
        foreach (var invalid in new[] { "not-a-number", string.Empty })
        {
            SourceInput(AutomationPortValueType.Number).Find("input").Change(invalid);
            page.Find("[data-automation-run-scenario]").HasAttribute("disabled").ShouldBeTrue();
            SourceInput(AutomationPortValueType.Number)
                .Find("input")
                .GetAttribute("value")
                .ShouldBe(invalid);
            SourceInput(AutomationPortValueType.Number)
                .Find("[role=alert]")
                .TextContent.ShouldNotBeNullOrWhiteSpace();
            await using (var db = await f.Database.CreateDbContextAsync())
            {
                (await db.AutomationTraces.CountAsync()).ShouldBe(1);
            }
            SourceInput(AutomationPortValueType.Number).Find("input").Change("42");
        }
        await RunAndExpect("cheer-selected");
        panel = page.FindComponent<AutomationScenarioPanel>();
        panel.Instance.Editor!.Fixture.Context.Actor.ShouldBeNull();
        panel
            .Instance.Editor.Fixture.Context.Variables.ForExecution()[new("bits")]
            .Value.ShouldBe(new AutomationValue.Number(42));
        await Select(follow.Id);
        panel = page.FindComponent<AutomationScenarioPanel>();
        panel
            .Instance.Editor!.Fixture.Context.Variables.ForExecution()
            .ContainsKey(new("bits"))
            .ShouldBeFalse();
        page.FindAll("[data-automation-trace]").ShouldBeEmpty();
        await using var final = await f.Database.CreateDbContextAsync();
        (await final.AutomationFlowRuns.CountAsync()).ShouldBe(0);
        (await final.AutomationScenarios.CountAsync()).ShouldBe(0);

        IRenderedComponent<AutomationFixtureValueInput> SourceInput(AutomationPortValueType type) =>
            page.FindComponents<AutomationFixtureValueInput>()
                .Single(input => input.Instance.ValueType == type);
        async Task Select(AutomationNodeId source) =>
            await page.InvokeAsync(() =>
                page.FindComponent<AutomationScenarioPanel>()
                    .Instance.SourceChanged.InvokeAsync(source.Value.ToString())
            );
        async Task RunAndExpect(string message)
        {
            page.Find("[data-automation-run-scenario]").Click();
            page.WaitForAssertion(
                () =>
                    page.FindComponent<AutomationTracePanel>().Instance.Snapshot.ShouldNotBeNull(),
                TimeSpan.FromSeconds(20)
            );
            page.WaitForAssertion(
                () =>
                    page.FindComponent<AutomationTracePanel>()
                        .Instance.Snapshot!.Events.ShouldContain(entry =>
                            entry.Event.Kind == AutomationTraceEventKind.Terminal
                        ),
                TimeSpan.FromSeconds(20)
            );
            var trace = page.FindComponent<AutomationTracePanel>().Instance;
            var result = trace.Presentation!.Rows.Last(row =>
                row.Entry.Event.Kind == AutomationTraceEventKind.Result
            );
            await page.InvokeAsync(() => trace.Select.InvokeAsync(result.Entry.Sequence));
            page.FindComponent<AutomationTracePanel>()
                .Instance.Preview!.WouldSend.ShouldBe(
                    message,
                    string.Join(
                        " | ",
                        trace.Snapshot!.Events.Select(entry =>
                            $"{entry.Sequence}:{entry.Event.Kind}/{entry.Event.Outcome}/{entry.Event.Diagnostic}"
                        )
                    )
                );
            trace.Snapshot!.Events.ShouldContain(entry =>
                entry.Event.Kind == AutomationTraceEventKind.Branch
                && entry.Event.SelectedPort == new AutomationPortId("yes")
            );
            await page.InvokeAsync(trace.Close.InvokeAsync);
        }
    }

    [Test]
    public async Task AuthoringUi_HeldPriorSourceCannotRestoreItsInputsOrPreviewAfterSwitch()
    {
        var (editor, follow, cheer) = ManualInputEditor();
        var gate = new AuthoringCommitGate();
        await using var f = await AutomationEditorPageFixture.CreateAsync(
            authored: editor,
            interceptors: [gate]
        );
        var page = f.Page;
        page.Find("[data-automation-test-flow]").Click();
        await page.InvokeAsync(() =>
            page.FindComponent<AutomationScenarioPanel>()
                .Instance.SourceChanged.InvokeAsync(follow.Id.Value.ToString())
        );
        gate.Armed = true;
        var running = page.Find("[data-automation-run-scenario]")
            .ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
        try
        {
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await page.InvokeAsync(() =>
                page.FindComponent<AutomationScenarioPanel>()
                    .Instance.SourceChanged.InvokeAsync(cheer.Id.Value.ToString())
            );
        }
        finally
        {
            _ = gate.Release.TrySetResult();
        }
        await running;
        var input = page.FindComponent<AutomationScenarioPanel>().Instance.Editor!;
        input.Fixture.SourceNodeId.ShouldBe(cheer.Id);
        input.RawCommand.ShouldBe(string.Empty);
        input.Fixture.ConnectedInputs.ShouldBeEmpty();
        input
            .Fixture.Context.Variables.ForExecution()
            .ContainsKey(new("followed_at"))
            .ShouldBeFalse();
        page.FindAll("[data-automation-trace]").ShouldBeEmpty();
        page.FindAll(".automation-authoring-openers button").Last().Click();
        page.WaitForAssertion(() =>
            page.FindComponent<AutomationTracePanel>().Instance.Snapshot.ShouldNotBeNull()
        );
        page.FindComponent<AutomationTracePanel>().Instance.Preview.ShouldBeNull();
    }

    private static (
        AutomationEditorState Editor,
        AutomationEditorNode Follow,
        AutomationEditorNode Cheer
    ) ManualInputEditor()
    {
        var editor = AutomationEditorState.Create("Manual entry inputs");
        var definitions = ProductionDefinitions();
        var follow = editor.AddNode(
            definitions.Single(value => value.Id == AutomationDefinitionIds.FollowSource)
        );
        var cheer = editor.AddNode(
            definitions.Single(value => value.Id == AutomationDefinitionIds.CheerSource)
        );
        cheer.SetValue(new("minimum-bits"), "1");
        Branch(follow, "actor.display_name == 'Authored viewer'", "follow-selected");
        Branch(cheer, "bits == 42 && is_anonymous", "cheer-selected");
        return (editor, follow, cheer);

        void Branch(AutomationEditorNode source, string expression, string message)
        {
            var condition = editor.AddNode(
                definitions.Single(value => value.Id == AutomationDefinitionIds.ConditionControl)
            );
            condition.SetBindingMode(new("predicate"), AutomationInputBindingMode.Expression);
            condition.SetExpression(
                new("predicate"),
                new(AutomationExpressionLanguage.CurrentVersion, expression)
            );
            var action = editor.AddNode(
                definitions.Single(value => value.Id == AutomationDefinitionIds.SendChatAction)
            );
            action.SetValue(new("message"), message);
            editor.Edges.Add(
                new(
                    Guid.NewGuid(),
                    AutomationEdgeKind.Flow,
                    source.Id,
                    new("flow"),
                    condition.Id,
                    new("flow")
                )
            );
            editor.Edges.Add(
                new(
                    Guid.NewGuid(),
                    AutomationEdgeKind.Flow,
                    condition.Id,
                    new("yes"),
                    action.Id,
                    new("flow")
                )
            );
        }
    }
}
