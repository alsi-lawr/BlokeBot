using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.Automations.Page;
using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class AutomationEditorInteractionTests
{
    [Test]
    public async Task EffectsUi_CancelAndStaleSavedGraphRequireFreshApprovalAndDirtyDraftCannotEnable()
    {
        await using var f = await AutomationEditorPageFixture.CreateAsync();
        var page = f.Page;
        var header = page.FindComponent<AutomationEditorHeader>();
        await page.InvokeAsync(header.Instance.ToggleEnabled.InvokeAsync);
        page.WaitForAssertion(() =>
            page.FindComponent<AutomationEditorHeader>().Instance.EnableConfirmation.ShouldBeTrue()
        );
        page.Find(".automation-enable-warning button").Click();
        page.FindComponent<AutomationEditorHeader>().Instance.EnableConfirmation.ShouldBeFalse();
        await page.InvokeAsync(header.Instance.ToggleEnabled.InvokeAsync);
        var flows = f.Context.Services.GetRequiredService<AutomationFlowService>();
        AutomationFlowDraft graph;
        await using (var db = await f.Database.CreateDbContextAsync())
        {
            graph = AutomationFlowService
                .RestoreDraft(
                    await db
                        .AutomationFlows.Include(flow => flow.Nodes)
                        .Include(flow => flow.Edges)
                        .SingleAsync()
                )
                .ShouldBeOfType<AutomationFlowDraftRestoreOutcome.Available>()
                .Draft;
        }
        var action = graph.Nodes.Single(node =>
            node.Definition.TypeId == AutomationDefinitionIds.SendChatAction.Value
        );
        var changed = graph with
        {
            Nodes = graph.Nodes.Replace(
                action,
                action with
                {
                    FailurePolicy = AutomationNodeFailurePolicy.Continue,
                }
            ),
        };
        _ = (
            await flows.SaveAsync(changed, default)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        await page.InvokeAsync(header.Instance.ToggleEnabled.InvokeAsync);
        page.FindComponent<AutomationEditorHeader>().Instance.EnableConfirmation.ShouldBeTrue();
        await using (var db = await f.Database.CreateDbContextAsync())
        {
            (await db.AutomationFlows.SingleAsync()).IsEnabled.ShouldBeFalse();
        }
        await page.InvokeAsync(header.Instance.ToggleEnabled.InvokeAsync);
        page.WaitForAssertion(() =>
            page.FindComponent<AutomationEditorHeader>().Instance.Enabled.ShouldBeTrue()
        );
        await page.InvokeAsync(() =>
            page.FindComponent<AutomationEditorHeader>().Instance.ToggleEnabled.InvokeAsync()
        );
        page.WaitForAssertion(() =>
            page.FindComponent<AutomationEditorHeader>().Instance.Enabled.ShouldBeFalse()
        );
        page.Find("#automation-flow-name").Input("Unsaved editor change");
        await page.InvokeAsync(() =>
            page.FindComponent<AutomationEditorHeader>().Instance.ToggleEnabled.InvokeAsync()
        );
        await using var verify = await f.Database.CreateDbContextAsync();
        (await verify.AutomationFlows.SingleAsync()).IsEnabled.ShouldBeFalse();
        page.FindComponent<AutomationEditorHeader>().Instance.HasChanges.ShouldBeTrue();
    }
}
