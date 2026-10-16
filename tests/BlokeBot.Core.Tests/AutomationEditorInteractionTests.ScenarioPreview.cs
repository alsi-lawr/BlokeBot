using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.Automations.Page;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class AutomationEditorInteractionTests
{
    [Test]
    public async Task AuthoringUi_CurrentValuesExpireOnInputCloseAndHistoryReopen()
    {
        await using var fixture = await AutomationEditorPageFixture.CreateAsync();
        var page = fixture.Page;
        page.Find("[data-automation-test-flow]").Click();
        async Task<AutomationTracePanel> Run()
        {
            page.Find("[data-automation-run-scenario]").Click();
            page.WaitForAssertion(
                () =>
                    page.FindComponent<AutomationTracePanel>().Instance.Snapshot.ShouldNotBeNull(),
                TimeSpan.FromSeconds(20)
            );
            var trace = page.FindComponent<AutomationTracePanel>().Instance;
            await page.InvokeAsync(() =>
                trace.Select.InvokeAsync(
                    trace
                        .Presentation!.Rows.Single(row =>
                            row.Entry.Event.Kind == AutomationTraceEventKind.Result
                        )
                        .Entry.Sequence
                )
            );
            _ = page.FindComponent<AutomationTracePanel>()
                .Instance.Preview!.WouldSend.ShouldNotBeNull();
            return page.FindComponent<AutomationTracePanel>().Instance;
        }
        var first = (await Run()).Snapshot!.Id;
        page.FindComponent<AutomationScenarioPanel>()
            .Find("input[type=datetime-local]")
            .Change("2026-10-16T12:00:00");
        page.FindComponent<AutomationTracePanel>().Instance.Preview.ShouldBeNull();
        var second = await Run();
        second.Snapshot!.Id.ShouldNotBe(first);
        await page.InvokeAsync(second.Close.InvokeAsync);
        page.FindAll("[data-automation-trace]").ShouldBeEmpty();
        page.FindAll(".automation-authoring-openers button").Last().Click();
        page.WaitForAssertion(() =>
            page.FindComponent<AutomationTracePanel>().Instance.Snapshot.ShouldNotBeNull()
        );
        var history = page.FindComponent<AutomationTracePanel>().Instance;
        await page.InvokeAsync(() =>
            history.Select.InvokeAsync(
                history
                    .Presentation!.Rows.Single(row =>
                        row.Entry.Event.Kind == AutomationTraceEventKind.Result
                    )
                    .Entry.Sequence
            )
        );
        page.FindComponent<AutomationTracePanel>().Instance.Preview.ShouldBeNull();
        var third = await Run();
        page.Find("[data-automation-authoring]").KeyDown(new KeyboardEventArgs { Key = "Escape" });
        page.FindComponent<AutomationTracePanel>().Instance.Preview.ShouldBeNull();
        fixture.Context.JSInterop.Invocations.ShouldContain(call =>
            call.Identifier == "focusAuthoringOpener"
        );
        await page.InvokeAsync(() =>
            third.SelectRun.InvokeAsync(third.Snapshot!.Id.Value.ToString())
        );
        page.FindComponent<AutomationTracePanel>().Instance.Preview.ShouldBeNull();
    }

    [Test]
    public async Task AuthoringUi_ClosingTestWhileRunIsHeldCannotReopenOrRepopulateCurrentValues()
    {
        var gate = new AuthoringCommitGate();
        await using var fixture = await AutomationEditorPageFixture.CreateAsync(
            interceptors: [gate]
        );
        var page = fixture.Page;
        page.Find("[data-automation-test-flow]").Click();
        gate.Armed = true;
        var running = page.Find("[data-automation-run-scenario]").ClickAsync(new MouseEventArgs());
        try
        {
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            page.Find("[data-automation-authoring]")
                .KeyDown(new KeyboardEventArgs { Key = "Escape" });
        }
        finally
        {
            _ = gate.Release.TrySetResult();
        }
        await running;
        page.FindAll("[data-automation-trace]").ShouldBeEmpty();
        page.FindAll("[data-automation-authoring]").ShouldBeEmpty();
        page.FindAll(".automation-authoring-openers button").Last().Click();
        page.WaitForAssertion(
            () => page.FindComponent<AutomationTracePanel>().Instance.Snapshot.ShouldNotBeNull(),
            TimeSpan.FromSeconds(20)
        );
        page.FindComponent<AutomationTracePanel>().Instance.Preview.ShouldBeNull();
    }
}
