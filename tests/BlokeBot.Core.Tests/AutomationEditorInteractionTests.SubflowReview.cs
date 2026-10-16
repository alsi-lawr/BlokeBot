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
    public async Task SubflowReviewUi_ChangedPublishedBaselineRetainsDirtyDraftUntilExplicitRenewal()
    {
        await using var f = await AutomationEditorPageFixture.CreateAsync();
        var original = await PublishBoundaryFixtureAsync(f);
        var page = f.Page;
        await OpenBoundaryFixtureAsync(page, original.SubflowId);
        page.FindComponent<AutomationBoundaryPorts>()
            .Find("[data-automation-port-label=message]")
            .Change("My unsaved label");
        var draft = CanvasDraft(page.FindComponent<AutomationFlowCanvas>().Instance);
        page.Find("[data-automation-save-flow]").Click();
        page.WaitForAssertion(() =>
            page.Find("[data-automation-publish-subflow]").ShouldNotBeNull()
        );
        var newer = await AdvanceReviewedRevisionAsync(f, original);
        page.Find("[data-automation-publish-subflow]").Click();
        page.WaitForAssertion(() =>
            page.FindAll("[data-automation-publish-subflow]").ShouldBeEmpty()
        );
        page.Find("[data-automation-review-subflow]").HasAttribute("disabled").ShouldBeFalse();
        CanvasDraft(page.FindComponent<AutomationFlowCanvas>().Instance).ShouldBe(draft);
        page.FindComponent<AutomationEditorHeader>().Instance.HasChanges.ShouldBeTrue();
        var service = f.Context.Services.GetRequiredService<AutomationSubflowService>();
        (
            await service.LoadCurrentAsync(original.Graph.HostId, original.SubflowId, default)
        )!.Id.ShouldBe(newer.Id);
        page.Find("[data-automation-review-subflow]").Click();
        page.WaitForAssertion(() =>
            page.Find("[data-automation-publish-subflow]").ShouldNotBeNull()
        );
        page.Find("[data-automation-publish-subflow]").Click();
        page.WaitForAssertion(() =>
            page.FindComponent<AutomationEditorHeader>().Instance.HasChanges.ShouldBeFalse()
        );
        var saved = (
            await service.LoadCurrentAsync(original.Graph.HostId, original.SubflowId, default)
        ).ShouldNotBeNull();
        saved.Revision.ShouldBe(newer.Revision + 1);
        saved.Interface.Inputs[0].Name.ShouldBe("My unsaved label");
        saved.Graph.Edges.ShouldBe(original.Graph.Edges);
    }

    [Test]
    public async Task SubflowReviewUi_ExtractionChildChangeRefusesBeforePublishAndReplacementThenRefreshIsUndoable()
    {
        await using var f = await AutomationEditorPageFixture.CreateAsync();
        var target = await PublishNodeTargetAsync(f, "Extraction dependency");
        var page = f.Page;
        var call = await AddInspectorCallAsync(page);
        await page.InvokeAsync(() =>
            page.FindComponent<AutomationCallTarget>()
                .Instance.Selected.InvokeAsync(target.SubflowId)
        );
        var canvas = page.FindComponent<AutomationFlowCanvas>().Instance;
        var source = canvas.Nodes.Single(n => n.Definition.Kind == AutomationNodeKind.Source);
        var action = canvas.Nodes.Single(n => n.Id != source.Id && n.Id != call.Id);
        foreach (var edge in canvas.Edges.ToArray())
        {
            await page.InvokeAsync(() => canvas.DeleteEdge.InvokeAsync(edge.Id));
        }
        await page.InvokeAsync(() =>
            canvas.Connect.InvokeAsync(new(source.Id, new("flow"), call.Id, new("flow")))
        );
        await page.InvokeAsync(() =>
            canvas.Connect.InvokeAsync(new(call.Id, new("complete"), action.Id, new("flow")))
        );
        await page.InvokeAsync(() => canvas.SelectionChanged.InvokeAsync(new([call.Id], null)));
        var draft = CanvasDraft(page.FindComponent<AutomationFlowCanvas>().Instance);
        page.Find("[data-automation-extract-selection]").Click();
        page.WaitForAssertion(() =>
            page.Find("[data-automation-apply-extraction]").HasAttribute("disabled").ShouldBeFalse()
        );
        _ = await AdvanceReviewedRevisionAsync(f, target);
        await using var db = await f.Database.CreateDbContextAsync();
        var versions = await db.AutomationSubflowRevisions.CountAsync();
        page.Find("[data-automation-apply-extraction]").Click();
        page.WaitForAssertion(() =>
            page.Find("[data-automation-apply-extraction]").HasAttribute("disabled").ShouldBeTrue()
        );
        CanvasDraft(page.FindComponent<AutomationFlowCanvas>().Instance).ShouldBe(draft);
        (await db.AutomationSubflowRevisions.CountAsync()).ShouldBe(versions);
        page.Find("[data-automation-extraction] button").Click();
        page.WaitForAssertion(() =>
            page.Find("[data-automation-apply-extraction]").HasAttribute("disabled").ShouldBeFalse()
        );
        page.Find("[data-automation-apply-extraction]").Click();
        page.WaitForAssertion(() => page.FindAll("[data-automation-extraction]").ShouldBeEmpty());
        (await db.AutomationSubflowRevisions.CountAsync()).ShouldBe(versions + 1);
        CanvasDraft(page.FindComponent<AutomationFlowCanvas>().Instance).ShouldNotBe(draft);
        await page.Instance.ApplyEditorHistoryShortcutAsync("undo");
        CanvasDraft(page.FindComponent<AutomationFlowCanvas>().Instance).ShouldBe(draft);
        await page.Instance.ApplyEditorHistoryShortcutAsync("redo");
        CanvasDraft(page.FindComponent<AutomationFlowCanvas>().Instance).ShouldNotBe(draft);
    }

    private static async Task<AutomationSubflowRevision> AdvanceReviewedRevisionAsync(
        AutomationEditorPageFixture f,
        AutomationSubflowRevision revision
    )
    {
        var service = f.Context.Services.GetRequiredService<AutomationSubflowService>();
        var draft = new AutomationSubflowDraft(
            revision.SubflowId,
            "Concurrent metadata publication",
            revision.Interface,
            revision.Graph
        );
        var ready = (
            await service.PreviewAsync(draft, default)
        ).ShouldBeOfType<AutomationSubflowPreviewOutcome.Ready>();
        return (await service.PublishAsync(draft, ready.Reviewed, default))
            .ShouldBeOfType<AutomationSubflowPublishOutcome.Published>()
            .Revision;
    }
}
