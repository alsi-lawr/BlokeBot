using System.Data.Common;
using BlokeBot.Core.Auth.Sessions;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.Automations.Page;
using BlokeBot.Core.Hosts;
using BlokeBot.Eventing;
using BlokeBot.Persistence.Models;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class AutomationEditorInteractionTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AuthoringUi_LibraryBrowsingPreservesDirtyGraphSelectionViewportAndHistory(
        bool focusMode
    )
    {
        await using var fixture = await AutomationEditorPageFixture.CreateAsync();
        var target = await PublishNodeTargetAsync(fixture, "Reusable greeting");
        var page = fixture.Page;
        if (focusMode)
        {
            page.Find("[aria-label=Focus]").Click();
        }
        var source = page.FindComponent<AutomationFlowCanvas>().Instance.Nodes[0];
        await DiscloseAsync(page, source.Id);
        NodeSelector(page, source.Id).KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        page.Find("#automation-flow-name").Input("Unsaved ordinary");
        var canvas = page.FindComponent<AutomationFlowCanvas>().Instance;
        var draft = CanvasDraft(canvas);
        var viewport = canvas.ViewportKey;
        var selected = canvas.SelectedNodeIds.ToArray();
        _ = await fixture
            .Context.Services.GetRequiredService<EventBus<AppEventKind>>()
            .PublishAsync(AppEventKind.HostedChannelsChanged, CancellationToken.None);
        await BrowseLibraryAsync(page, AutomationLibraryKind.Subflows);
        page.FindAll(".automation-dirty-dialog").ShouldBeEmpty();
        page.Find("#automation-flow-name").GetAttribute("value").ShouldBe("Unsaved ordinary");
        CanvasDraft(page.FindComponent<AutomationFlowCanvas>().Instance).ShouldBe(draft);
        page.FindComponent<AutomationFlowCanvas>().Instance.ViewportKey.ShouldBe(viewport);
        page.FindComponent<AutomationFlowCanvas>().Instance.SelectedNodeIds.ShouldBe(selected);
        await page.InvokeAsync(() =>
            page.FindComponent<AutomationFlowRail>()
                .Instance.SelectSubflow.InvokeAsync(target.SubflowId)
        );
        _ = page.Find(".automation-dirty-dialog").ShouldNotBeNull();
        await page.InvokeAsync(
            page.FindComponent<AutomationDirtyDialog>().Instance.Cancel.InvokeAsync
        );
        await page.Instance.ApplyEditorHistoryShortcutAsync("undo");
        page.Find("#automation-flow-name").GetAttribute("value").ShouldBe("Available flow");
        await page.Instance.ApplyEditorHistoryShortcutAsync("redo");
        page.Find("#automation-flow-name").GetAttribute("value").ShouldBe("Unsaved ordinary");
        await page.InvokeAsync(() =>
            page.FindComponent<AutomationFlowRail>()
                .Instance.SelectSubflow.InvokeAsync(target.SubflowId)
        );
        await page.InvokeAsync(
            page.FindComponent<AutomationDirtyDialog>().Instance.Discard.InvokeAsync
        );
        page.WaitForAssertion(() =>
            page.FindComponent<AutomationFlowRail>().Instance.EditingSubflow.ShouldBeTrue()
        );
        page.Find("#automation-flow-name").Input("Unsaved subflow");
        var subflowDraft = CanvasDraft(page.FindComponent<AutomationFlowCanvas>().Instance);
        await BrowseLibraryAsync(page, AutomationLibraryKind.Flows);
        page.FindComponent<AutomationFlowRail>().Instance.EditingSubflow.ShouldBeTrue();
        page.FindAll(".automation-flow-rail-actions").ShouldBeEmpty();
        page.FindAll(".automation-flow-item--active").ShouldBeEmpty();
        CanvasDraft(page.FindComponent<AutomationFlowCanvas>().Instance).ShouldBe(subflowDraft);
        page.FindAll(".automation-dirty-dialog").ShouldBeEmpty();
        page.Find(".automation-page-actions button").Click();
        _ = page.Find(".automation-dirty-dialog").ShouldNotBeNull();
        await page.InvokeAsync(
            page.FindComponent<AutomationDirtyDialog>().Instance.Cancel.InvokeAsync
        );
        page.Find("[data-automation-new-subflow]").Click();
        _ = page.Find(".automation-dirty-dialog").ShouldNotBeNull();
        await page.InvokeAsync(
            page.FindComponent<AutomationDirtyDialog>().Instance.Discard.InvokeAsync
        );
        page.WaitForAssertion(() =>
            page.FindComponent<AutomationFlowCanvas>().Instance.Nodes.Count.ShouldBe(2)
        );
        page.FindComponent<AutomationFlowRail>()
            .Instance.CurrentSubflowId.ShouldNotBe(target.SubflowId);
    }

    [Test]
    public async Task AuthoringUi_ToolboxCallSelectsTargetAsOneUndoableDraftOperation()
    {
        await using var fixture = await AutomationEditorPageFixture.CreateAsync();
        var target = await PublishNodeTargetAsync(fixture, "First greeting");
        var second = await PublishNodeTargetAsync(fixture, "Second greeting");
        var page = fixture.Page;
        var node = await AddInspectorCallAsync(page);
        node.Subflow.ShouldBeNull();
        var selector = page.FindComponent<AutomationCallTarget>().Instance;
        selector.HasTarget.ShouldBeFalse();
        selector.Choosing.ShouldBeTrue();
        await page.InvokeAsync(() => selector.Selected.InvokeAsync(target.SubflowId));
        page.WaitForAssertion(() =>
            page.FindComponent<AutomationCallTarget>().Instance.Choosing.ShouldBeFalse()
        );
        node.Subflow.ShouldBeOfType<AutomationSubflowInvocationConfiguration>()
            .SubflowId.ShouldBe(target.SubflowId);
        var id = node.Id;
        var position = node.Position;
        await page.Instance.ApplyEditorHistoryShortcutAsync("undo");
        page.FindComponent<AutomationFlowCanvas>()
            .Instance.Nodes.Single(value => value.Id == id)
            .Subflow.ShouldBeNull();
        page.FindComponent<AutomationFlowCanvas>().Instance.Nodes.Count.ShouldBe(3);
        await page.Instance.ApplyEditorHistoryShortcutAsync("redo");
        await page.InvokeAsync(() =>
            page.FindComponent<AutomationCallTarget>()
                .Instance.Selected.InvokeAsync(second.SubflowId)
        );
        var changed = page.FindComponent<AutomationFlowCanvas>()
            .Instance.Nodes.Single(value => value.Id == id);
        changed
            .Subflow.ShouldBeOfType<AutomationSubflowInvocationConfiguration>()
            .SubflowId.ShouldBe(second.SubflowId);
        changed.Position.ShouldBe(position);
        await page.Instance.ApplyEditorHistoryShortcutAsync("undo");
        page.FindComponent<AutomationFlowCanvas>()
            .Instance.Nodes.Single(value => value.Id == id)
            .Subflow.ShouldBeOfType<AutomationSubflowInvocationConfiguration>()
            .SubflowId.ShouldBe(target.SubflowId);
        (await fixture.PersistedFlowNameAsync()).ShouldBe("Available flow");
        await using var db = await fixture.Database.CreateDbContextAsync();
        (await db.AutomationFlowNodes.CountAsync()).ShouldBe(2);
        (await db.AutomationFlowRuns.CountAsync()).ShouldBe(0);
        (await db.AutomationTraces.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task AuthoringUi_OlderTargetAndSearchResponsesCannotReplaceNewerDraftOrQuery()
    {
        var gate = new CallReadGate();
        await using var fixture = await AutomationEditorPageFixture.CreateAsync(
            interceptors: [gate]
        );
        var first = await PublishNodeTargetAsync(fixture, "First greeting");
        var second = await PublishNodeTargetAsync(fixture, "Second greeting");
        var page = fixture.Page;
        var node = await AddInspectorCallAsync(page);
        gate.Arm();
        var older = page.InvokeAsync(() =>
            page.FindComponent<AutomationCallTarget>()
                .Instance.Selected.InvokeAsync(first.SubflowId)
        );
        try
        {
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await page.InvokeAsync(() =>
                page.FindComponent<AutomationCallTarget>()
                    .Instance.Selected.InvokeAsync(second.SubflowId)
            );
        }
        finally
        {
            _ = gate.Release.TrySetResult();
        }
        await older;
        node.Subflow.ShouldBeOfType<AutomationSubflowInvocationConfiguration>()
            .SubflowId.ShouldBe(second.SubflowId);
        await page.InvokeAsync(
            page.FindComponent<AutomationCallTarget>().Instance.Change.InvokeAsync
        );
        gate.Arm();
        var search = page.InvokeAsync(() =>
            page.FindComponent<AutomationCallTarget>().Instance.SearchChanged.InvokeAsync("First")
        );
        try
        {
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await page.InvokeAsync(() =>
                page.FindComponent<AutomationCallTarget>()
                    .Instance.SearchChanged.InvokeAsync("Second")
            );
        }
        finally
        {
            _ = gate.Release.TrySetResult();
        }
        await search;
        page.FindComponent<AutomationCallTarget>()
            .Instance.Page.Subflows.ShouldHaveSingleItem()
            .Id.ShouldBe(second.SubflowId);
        page.Find(".automation-call-target input[type=search]")
            .GetAttribute("value")
            .ShouldBe("Second");
        page.FindComponent<AutomationCallTarget>().Instance.Name.ShouldBe(second.Graph.Name);
        gate.Arm();
        var staleDraft = page.InvokeAsync(() =>
            page.FindComponent<AutomationCallTarget>()
                .Instance.Selected.InvokeAsync(first.SubflowId)
        );
        try
        {
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            page.Find("#automation-node-display-alias").Input("Newer node edit");
        }
        finally
        {
            _ = gate.Release.TrySetResult();
        }
        await staleDraft;
        node.DisplayAlias.ShouldBe("Newer node edit");
        node.Subflow.ShouldBeOfType<AutomationSubflowInvocationConfiguration>()
            .SubflowId.ShouldBe(second.SubflowId);
    }

    [Test]
    public async Task AuthoringUi_TargetLoadsCannotMutateAnotherNodeUndoneDraftOrHost()
    {
        var gate = new CallReadGate();
        await using var fixture = await AutomationEditorPageFixture.CreateAsync(
            interceptors: [gate]
        );
        var target = await PublishNodeTargetAsync(fixture, "Greeting");
        var page = fixture.Page;
        var node = await AddInspectorCallAsync(page);
        var source = page.FindComponent<AutomationFlowCanvas>().Instance.Nodes[0];
        gate.Arm();
        var otherNode = page.InvokeAsync(() =>
            page.FindComponent<AutomationCallTarget>()
                .Instance.Selected.InvokeAsync(target.SubflowId)
        );
        try
        {
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await DiscloseAsync(page, source.Id);
        }
        finally
        {
            _ = gate.Release.TrySetResult();
        }
        await otherNode;
        node.Subflow.ShouldBeNull();
        await DiscloseAsync(page, node.Id);
        gate.Arm();
        var undone = page.InvokeAsync(() =>
            page.FindComponent<AutomationCallTarget>()
                .Instance.Selected.InvokeAsync(target.SubflowId)
        );
        try
        {
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await page.Instance.ApplyEditorHistoryShortcutAsync("undo");
        }
        finally
        {
            _ = gate.Release.TrySetResult();
        }
        await undone;
        page.FindComponent<AutomationFlowCanvas>()
            .Instance.Nodes.ShouldNotContain(value => value.Id == node.Id);
        await page.Instance.ApplyEditorHistoryShortcutAsync("redo");
        await DiscloseAsync(page, node.Id);
        await using var db = await fixture.Database.CreateDbContextAsync();
        var host = new BotHost
        {
            TwitchUserId = "node-target-other",
            Login = "other",
            DisplayName = "Other",
            EnabledFeatures = HostFeatureFlags.Automations,
        };
        _ = db.Hosts.Add(host);
        _ = await db.SaveChangesAsync();
        var choice = new BotHostChoice(host.Id, "other", "Other", AuthRole.Streamer);
        gate.Arm();
        var otherHost = page.InvokeAsync(() =>
            page.FindComponent<AutomationCallTarget>()
                .Instance.Selected.InvokeAsync(target.SubflowId)
        );
        try
        {
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            _ = fixture.Authorization.SetClaims(
                TestPrincipals
                    .BlokeBotUser(
                        "other",
                        role: AuthRole.Streamer,
                        availableHosts: [choice],
                        selectedHost: choice
                    )
                    .Claims.ToArray()
            );
            _ = await fixture
                .Context.Services.GetRequiredService<EventBus<AppEventKind>>()
                .PublishAsync(AppEventKind.HostedChannelsChanged, CancellationToken.None);
            page.WaitForAssertion(() =>
                page.FindAll("[data-automation-inspector]").ShouldBeEmpty()
            );
        }
        finally
        {
            _ = gate.Release.TrySetResult();
        }
        await otherHost;
        page.FindAll("[data-automation-inspector]").ShouldBeEmpty();
        (await db.AutomationFlowRuns.CountAsync()).ShouldBe(0);
    }

    private static Task BrowseLibraryAsync(
        IRenderedComponent<AutomationEditorPage> page,
        AutomationLibraryKind kind
    ) =>
        page.InvokeAsync(() =>
            page.FindComponent<AutomationFlowRail>().Instance.LibraryChanged.InvokeAsync(kind)
        );

    private static async Task<AutomationEditorNode> AddInspectorCallAsync(
        IRenderedComponent<AutomationEditorPage> page
    )
    {
        await page.InvokeAsync(
            page.FindComponent<AutomationWorkspaceToolbar>().Instance.ToggleToolbox.InvokeAsync
        );
        var toolbox = page.FindComponent<AutomationToolbox>().Instance;
        var definition = toolbox.Definitions.Single(value =>
            value.Id.Value == AutomationSubflowDefinitions.Invoke
        );
        await page.InvokeAsync(() => toolbox.Add.InvokeAsync(definition));
        page.WaitForAssertion(() =>
            page.FindComponent<AutomationCallTarget>().Instance.Loading.ShouldBeFalse()
        );
        return page.FindComponent<AutomationNodeInspector>().Instance.Node!;
    }

    private static async Task<AutomationSubflowRevision> PublishNodeTargetAsync(
        AutomationEditorPageFixture fixture,
        string name
    )
    {
        var catalogue = fixture.Context.Services.GetRequiredService<AutomationCatalogService>();
        await using var db = await fixture.Database.CreateDbContextAsync();
        var host = await db.Hosts.Select(value => value.Id).SingleAsync();
        var contract = new AutomationSubflowInterface([], []);
        var entry = AutomationEditorNode.FromDefinition(
            AutomationSubflowDefinitions.Boundary(AutomationSubflowDefinitions.Entry, contract),
            catalogue,
            default
        );
        var exit = AutomationEditorNode.FromDefinition(
            AutomationSubflowDefinitions.Boundary(AutomationSubflowDefinitions.Exit, contract),
            catalogue,
            default
        );
        var draft = new AutomationSubflowDraft(
            new(Guid.NewGuid()),
            "Node target fixture",
            contract,
            new(
                null,
                new(host),
                name,
                1,
                false,
                [entry.Draft(), exit.Draft()],
                [
                    new(
                        Guid.NewGuid(),
                        AutomationEdgeKind.Flow,
                        entry.Id,
                        new("complete"),
                        exit.Id,
                        new("flow")
                    ),
                ]
            )
        );
        return (
            await fixture
                .Context.Services.GetRequiredService<AutomationSubflowService>()
                .PublishAsync(draft, CancellationToken.None)
        )
            .ShouldBeOfType<AutomationSubflowPublishOutcome.Published>()
            .Revision;
    }

    private sealed class CallReadGate : DbCommandInterceptor
    {
        private bool _armed;
        internal TaskCompletionSource Entered { get; private set; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; private set; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal void Arm()
        {
            Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _armed = true;
        }

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default
        )
        {
            if (
                _armed
                && command.CommandText.Contains("automation_subflows", StringComparison.Ordinal)
            )
            {
                _armed = false;
                _ = Entered.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }
}
