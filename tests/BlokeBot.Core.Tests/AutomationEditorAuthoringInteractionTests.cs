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
    public async Task AuthoringUi_ChangingTabsDoesNotStealTheSegmentedControlsFocus()
    {
        await using var fixture = await AutomationEditorPageFixture.CreateAsync();
        fixture.Page.Find("[data-automation-test-flow]").Click();
        fixture.Page.WaitForAssertion(() =>
            fixture.Context.JSInterop.Invocations.ShouldContain(call =>
                call.Identifier == "focusAuthoring"
            )
        );
        var paneFocusCalls = fixture.Context.JSInterop.Invocations.Count(call =>
            call.Identifier == "focusAuthoring"
        );
        fixture
            .Page.Find(".automation-authoring-tabs [aria-selected=true]")
            .KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        fixture.Page.WaitForAssertion(() =>
            fixture.Page.Find("[data-automation-subflow-library]").ShouldNotBeNull()
        );
        fixture
            .Context.JSInterop.Invocations.Count(call => call.Identifier == "focusAuthoring")
            .ShouldBe(paneFocusCalls);
        fixture
            .Page.Find(".automation-authoring-tabs [aria-selected=true]")
            .KeyDown(new KeyboardEventArgs { Key = "Escape" });
        fixture.Page.WaitForAssertion(() =>
            fixture.Context.JSInterop.Invocations.ShouldContain(call =>
                call.Identifier == "focusAuthoringOpener"
            )
        );
    }

    [Test]
    public async Task AuthoringUi_UnsavedDraftRunKeepsProductionFlowAndSelectsTraceNode()
    {
        await using var fixture = await AutomationEditorPageFixture.CreateAsync();
        var page = fixture.Page;
        page.Find("#automation-flow-name").Input("Unsaved scenario draft");
        page.Find("[data-automation-test-flow]").Click();
        page.WaitForAssertion(() =>
            page.FindComponent<AutomationScenarioPanel>().Instance.Editor.ShouldNotBeNull()
        );
        page.Find("[data-automation-run-scenario]").Click();
        page.WaitForAssertion(
            () =>
                page.FindComponent<AutomationTracePanel>().Instance.Presentation.ShouldNotBeNull(),
            TimeSpan.FromSeconds(20)
        );
        (await fixture.PersistedFlowNameAsync()).ShouldBe("Available flow");
        await using var db = await fixture.Database.CreateDbContextAsync();
        (await db.AutomationFlowRuns.CountAsync()).ShouldBe(0);
        (await db.AutomationTraces.CountAsync()).ShouldBe(1);
        var trace = page.FindComponent<AutomationTracePanel>().Instance;
        var target = trace.Presentation!.Rows.Last(row => row.RootNodeId is not null);
        await page.InvokeAsync(() => trace.Select.InvokeAsync(target.Entry.Sequence));
        page.FindComponent<AutomationFlowCanvas>()
            .Instance.SelectedNodeIds.ShouldContain(target.RootNodeId!.Value);
        page.Find("[data-automation-authoring]").KeyDown(new KeyboardEventArgs { Key = "Escape" });
        page.WaitForAssertion(() =>
            fixture.Context.JSInterop.Invocations.ShouldContain(call =>
                call.Identifier == "focusAuthoringOpener"
            )
        );
    }

    [Test]
    public async Task AuthoringUi_ScenarioSaveAndReloadPreserveTypedClockAndFailureOutcome()
    {
        await using var fixture = await AutomationEditorPageFixture.CreateAsync();
        var page = fixture.Page;
        page.Find("[data-automation-test-flow]").Click();
        var panel = page.FindComponent<AutomationScenarioPanel>();
        panel.Find("input[type=datetime-local]").Change("2026-09-07T10:15:00");
        panel.Find("input[inputmode=numeric]").Change("71");
        panel.Find(".automation-effect-choice select").Change("Failed");
        page.Find("[data-automation-save-scenario]").Click();
        page.WaitForAssertion(() =>
            page.FindComponent<AutomationScenarioPanel>().Instance.Editor!.Id.ShouldNotBeNull()
        );
        var saved = page.FindComponent<AutomationScenarioPanel>().Instance.Editor!;
        var id = saved.Id!.Value;
        await page.InvokeAsync(panel.Instance.New.InvokeAsync);
        await page.InvokeAsync(() => panel.Instance.Select.InvokeAsync(id.Value.ToString()));
        var restored = page.FindComponent<AutomationScenarioPanel>().Instance.Editor!;
        restored.Fixture.Seed.ShouldBe(71UL);
        restored.Fixture.ClockUtc.ShouldBe(
            new DateTimeOffset(2026, 9, 7, 10, 15, 0, TimeSpan.Zero)
        );
        restored
            .Fixture.Effects.ShouldHaveSingleItem()
            .Result.ShouldBe(AutomationScenarioEffectResult.Failed);
        page.Find("[data-automation-run-scenario]").Click();
        page.WaitForAssertion(
            () => page.FindComponent<AutomationTracePanel>().Instance.Snapshot.ShouldNotBeNull(),
            TimeSpan.FromSeconds(20)
        );
        page.FindComponent<AutomationTracePanel>()
            .Instance.Presentation!.Rows.ShouldContain(row =>
                row.Entry.Event.Outcome == AutomationTraceOutcome.Failed
            );
    }

    [Test]
    public async Task AuthoringUi_DirtySubflowNavigationRequiresReviewBeforePublication()
    {
        await using var fixture = await AutomationEditorPageFixture.CreateAsync();
        var page = fixture.Page;
        page.Find("[data-automation-authoring-opener]").Click();
        page.Find("[data-automation-new-subflow]").Click();
        page.WaitForAssertion(() =>
            page.Find("[data-automation-subflow-editor]").ShouldNotBeNull()
        );
        page.Find(".automation-page-actions button").Click();
        page.Find(".automation-dirty-dialog .btn-primary").Click();
        page.WaitForAssertion(() =>
            page.Find("[data-automation-publication-preview]").ShouldNotBeNull()
        );
        await using (var db = await fixture.Database.CreateDbContextAsync())
        {
            (await db.AutomationSubflowRevisions.CountAsync()).ShouldBe(0);
        }
        page.Find("[data-automation-publish-subflow]").Click();
        page.WaitForAssertion(() =>
            page.FindAll("[data-automation-publication-preview]").ShouldBeEmpty()
        );
        await using var published = await fixture.Database.CreateDbContextAsync();
        (await published.AutomationSubflowRevisions.CountAsync()).ShouldBe(1);
        (await published.AutomationFlows.CountAsync()).ShouldBe(1);
    }

    [Test]
    public async Task AuthoringUi_LibraryPagingAndSearchReplaceRatherThanAccumulateResults()
    {
        await using var fixture = await AutomationEditorPageFixture.CreateAsync();
        var service = fixture.Context.Services.GetRequiredService<AutomationSubflowService>();
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
        var graph = new AutomationFlowDraft(
            null,
            new(host),
            "Findable",
            AutomationFlowSchema.CurrentVersion,
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
        );
        var draft = new AutomationSubflowDraft(
            new(Guid.NewGuid()),
            "Library fixture",
            contract,
            graph
        );
        for (var i = 0; i < AutomationSubflowService.LibraryPageSize + 1; i++)
        {
            _ = (
                await service.PublishAsync(
                    draft with
                    {
                        Id = new(Guid.NewGuid()),
                    },
                    CancellationToken.None
                )
            ).ShouldBeOfType<AutomationSubflowPublishOutcome.Published>();
        }
        fixture.Page.Find("[data-automation-authoring-opener]").Click();
        fixture.Page.WaitForAssertion(() =>
            fixture
                .Page.FindAll(".automation-subflow-library button")
                .Count.ShouldBe(AutomationSubflowService.LibraryPageSize)
        );
        fixture.Page.Find("[data-automation-library-next]").Click();
        fixture.Page.WaitForAssertion(() =>
            fixture.Page.FindAll(".automation-subflow-library button").Count.ShouldBe(1)
        );
        fixture.Page.Find("[data-automation-subflow-search]").Change("not-found");
        fixture.Page.WaitForAssertion(() =>
            fixture.Page.FindAll(".automation-subflow-library button").ShouldBeEmpty()
        );
        fixture.Page.Find("[data-automation-subflow-search]").Change("Findable");
        fixture.Page.WaitForAssertion(() =>
            fixture
                .Page.FindAll(".automation-subflow-library button")
                .Count.ShouldBe(AutomationSubflowService.LibraryPageSize)
        );
    }

    [Test]
    public async Task AuthoringUi_PublicationCompletionPreservesEditsMadeWhilePublishing()
    {
        var gate = new AuthoringCommitGate();
        await using var fixture = await AutomationEditorPageFixture.CreateAsync(
            interceptors: [gate]
        );
        var page = fixture.Page;
        page.Find("[data-automation-authoring-opener]").Click();
        page.Find("[data-automation-new-subflow]").Click();
        page.Find("[data-automation-review-subflow]").Click();
        page.WaitForAssertion(() =>
            page.Find("[data-automation-publish-subflow]").ShouldNotBeNull()
        );
        gate.Armed = true;
        var publishing = page.Find("[data-automation-publish-subflow]")
            .ClickAsync(new MouseEventArgs());
        try
        {
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            page.Find("#automation-flow-name").Input("Edited during publication");
        }
        finally
        {
            _ = gate.Release.TrySetResult();
        }
        await publishing;
        page.Find("#automation-flow-name")
            .GetAttribute("value")
            .ShouldBe("Edited during publication");
        await using var db = await fixture.Database.CreateDbContextAsync();
        (await db.AutomationSubflowRevisions.CountAsync()).ShouldBe(1);
        page.Find(".automation-page-actions button").Click();
        page.WaitForAssertion(() => page.Find(".automation-dirty-dialog").ShouldNotBeNull());
    }

    [Test]
    public async Task AuthoringUi_NewerTraceSelectionWinsWhenOlderReadCompletesLast()
    {
        var gate = new AuthoringTraceReadGate();
        await using var fixture = await AutomationEditorPageFixture.CreateAsync(
            interceptors: [gate]
        );
        var page = fixture.Page;
        page.Find("[data-automation-test-flow]").Click();
        page.Find("[data-automation-run-scenario]").Click();
        page.WaitForAssertion(
            () => page.FindComponent<AutomationTracePanel>().Instance.Snapshot.ShouldNotBeNull(),
            TimeSpan.FromSeconds(20)
        );
        var first = page.FindComponent<AutomationTracePanel>().Instance.Snapshot!.Id;
        page.Find("[data-automation-run-scenario]").Click();
        page.WaitForAssertion(
            () =>
                page.FindComponent<AutomationTracePanel>().Instance.Snapshot!.Id.ShouldNotBe(first),
            TimeSpan.FromSeconds(20)
        );
        var panel = page.FindComponent<AutomationTracePanel>().Instance;
        var second = panel.Snapshot!.Id;
        gate.Armed = true;
        var older = page.InvokeAsync(() => panel.SelectRun.InvokeAsync(first.Value.ToString()));
        try
        {
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await page.InvokeAsync(() => panel.SelectRun.InvokeAsync(second.Value.ToString()));
        }
        finally
        {
            _ = gate.Release.TrySetResult();
        }
        await older;
        page.FindComponent<AutomationTracePanel>().Instance.Snapshot!.Id.ShouldBe(second);
    }

    [Test]
    public async Task AuthoringUi_HostSwitchDiscardsPendingTraceHeaders()
    {
        var gate = new AuthoringTraceReadGate { Summaries = true };
        await using var fixture = await AutomationEditorPageFixture.CreateAsync(
            interceptors: [gate]
        );
        var page = fixture.Page;
        page.Find("[data-automation-test-flow]").Click();
        page.Find("[data-automation-run-scenario]").Click();
        page.WaitForAssertion(
            () => page.FindComponent<AutomationTracePanel>().Instance.Snapshot.ShouldNotBeNull(),
            TimeSpan.FromSeconds(20)
        );
        await page.InvokeAsync(
            page.FindComponent<AutomationTracePanel>().Instance.Close.InvokeAsync
        );
        await using var db = await fixture.Database.CreateDbContextAsync();
        var other = new BotHost
        {
            TwitchUserId = "other-host",
            Login = "other",
            DisplayName = "Other",
            EnabledFeatures = HostFeatureFlags.Automations,
        };
        _ = db.Hosts.Add(other);
        _ = await db.SaveChangesAsync();
        var choice = new BotHostChoice(other.Id, "other", "Other", AuthRole.Streamer);
        gate.Armed = true;
        var load = page.FindAll(".automation-authoring-openers button")
            .Last()
            .ClickAsync(new MouseEventArgs());
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
                page.FindAll(".automation-flow-item strong")
                    .ShouldNotContain(item => item.TextContent == "Available flow")
            );
        }
        finally
        {
            _ = gate.Release.TrySetResult();
        }
        await load;
        page.FindAll("[data-automation-trace]").ShouldBeEmpty();
    }

    private sealed class AuthoringCommitGate : DbTransactionInterceptor
    {
        internal bool Armed { get; set; }
        internal TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async Task TransactionCommittedAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default
        )
        {
            if (!Armed)
            {
                return;
            }
            Armed = false;
            _ = Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class AuthoringTraceReadGate : DbCommandInterceptor
    {
        internal bool Summaries { get; init; }
        internal bool Armed { get; set; }
        internal TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default
        )
        {
            if (
                Armed
                && (
                    Summaries
                        ? command.CommandText.Contains(
                            "automation_traces",
                            StringComparison.Ordinal
                        )
                            && !command.CommandText.Contains(
                                "automation_trace_events",
                                StringComparison.Ordinal
                            )
                        : command.CommandText.Contains(
                            "automation_trace_events",
                            StringComparison.Ordinal
                        )
                )
            )
            {
                Armed = false;
                _ = Entered.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }
}
