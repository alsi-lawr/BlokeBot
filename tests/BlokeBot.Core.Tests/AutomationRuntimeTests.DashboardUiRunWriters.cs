using BlokeBot.Core.Features.Automations;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class AutomationRuntimeTests
{
    [Test]
    public async Task DashboardUiEvents_AllRunSignalsReadCommittedAdmissionWaitingAndTerminal_NoRejectedOrLostLeaseSignals()
    {
        await using var f = await RuntimeFixture.CreateAsync();
        var changes = new ReadingCommittedRunChanges(f);
        var runtime = new AutomationRuntimeService(
            f.Database,
            f.Catalog,
            f.Flows,
            f.Actions,
            f.Clock,
            changes,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AutomationRuntimeService>.Instance
        );
        var source = Node("manual-run", """{"data":"go"}""");
        var delay = Node("delay", """{"duration-milliseconds":1000}""");
        var chat = Node("send-chat", """{"message":"done"}""");
        var flow = await f.SaveAsync(
            [source, delay, chat],
            [Edge(source, "flow", delay), Edge(delay, "complete", chat)]
        );
        var manual = new AutomationManualRunService(f.Database, runtime, f.Catalog, f.Clock);
        var admitted = (
            await manual.RunAsync(new(f.HostId), flow, CancellationToken.None)
        ).ShouldBeOfType<AutomationManualRunOutcome.Dispatched>();
        changes.States.ShouldBe([AutomationFlowRunState.Running, AutomationFlowRunState.Waiting]);
        var runId = admitted.Dispatch.RunIds.Single();
        await using (var db = await f.Database.CreateDbContextAsync())
        {
            var run = await db.AutomationFlowRuns.SingleAsync(r => r.Id == runId.Value);
            run.ExecutionLeaseId = Guid.NewGuid();
            _ = await db.SaveChangesAsync();
        }
        f.Clock.Advance(TimeSpan.FromSeconds(1));
        (await runtime.ResumeAsync(runId, CancellationToken.None)).Status.ShouldBe(
            AutomationResumeStatus.Waiting
        );
        changes.States.Count.ShouldBe(2);
        await using (var db = await f.Database.CreateDbContextAsync())
        {
            var run = await db.AutomationFlowRuns.SingleAsync(r => r.Id == runId.Value);
            run.ExecutionLeaseId = null;
            _ = await db.SaveChangesAsync();
        }
        (await runtime.ResumeAsync(runId, CancellationToken.None)).Status.ShouldBe(
            AutomationResumeStatus.Completed
        );
        changes.States.ShouldBe([
            AutomationFlowRunState.Running,
            AutomationFlowRunState.Waiting,
            AutomationFlowRunState.Running,
            AutomationFlowRunState.Completed,
        ]);
        _ = await f.Features.DisableAsync(
            f.HostId,
            HostFeatureFlags.Automations,
            CancellationToken.None
        );
        _ = (
            await manual.RunAsync(new(f.HostId), flow, CancellationToken.None)
        ).ShouldBeOfType<AutomationManualRunOutcome.Unavailable>();
        changes.States.Count.ShouldBe(4);
    }

    [Test]
    public async Task DashboardUiEvents_AdmissionRollbackPublishesNothing_AndActionFailureSignalsCommittedFailure()
    {
        var failure = new DashboardAdmissionSaveFailure();
        await using var f = await RuntimeFixture.CreateAsync(
            chatAdmissions: [false],
            databaseInterceptors: [failure]
        );
        var changes = new ReadingCommittedRunChanges(f);
        var runtime = new AutomationRuntimeService(
            f.Database,
            f.Catalog,
            f.Flows,
            f.Actions,
            f.Clock,
            changes,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AutomationRuntimeService>.Instance
        );
        var source = Node("manual-run", """{"data":"go"}""");
        var chat = Node("send-chat", """{"message":"done"}""");
        var flow = await f.SaveAsync([source, chat], [Edge(source, "flow", chat)]);
        var manual = new AutomationManualRunService(f.Database, runtime, f.Catalog, f.Clock);
        failure.Armed = true;
        _ = await Should.ThrowAsync<DbUpdateException>(() =>
            manual.RunAsync(new(f.HostId), flow, CancellationToken.None)
        );
        changes.States.ShouldBeEmpty();
        await using (var read = await f.Database.CreateDbContextAsync())
        {
            (await read.AutomationFlowRuns.CountAsync()).ShouldBe(0);
        }
        failure.Armed = false;
        _ = await manual.RunAsync(new(f.HostId), flow, CancellationToken.None);
        changes.States.ShouldBe([AutomationFlowRunState.Running, AutomationFlowRunState.Failed]);
    }

    [Test]
    [Arguments(AutomationFlowRunStatus.Running)]
    [Arguments(AutomationFlowRunStatus.Completed)]
    public async Task DashboardUiEvents_LeaseLostInsideOwnedLoopDoesNotPublishWithoutWrite(
        AutomationFlowRunStatus status
    )
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var chat = new RecordingChatSender(
            null,
            async (_, _, _) =>
            {
                entered.SetResult();
                await release.Task;
            }
        );
        await using var f = await RuntimeFixture.CreateAsync(chat: chat);
        var changes = new ReadingCommittedRunChanges(f);
        var runtime = new AutomationRuntimeService(
            f.Database,
            f.Catalog,
            f.Flows,
            f.Actions,
            f.Clock,
            changes,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AutomationRuntimeService>.Instance
        );
        var source = Node("manual-run", """{"data":"go"}""");
        var action = Node("send-chat", """{"message":"one effect"}""");
        var flow = await f.SaveAsync([source, action], [Edge(source, "flow", action)]);
        var operation = new AutomationManualRunService(
            f.Database,
            runtime,
            f.Catalog,
            f.Clock
        ).RunAsync(new(f.HostId), flow, CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Guid runId;
        var replacementLease = Guid.NewGuid();
        try
        {
            await using var db = await f.Database.CreateDbContextAsync();
            var run = await db.AutomationFlowRuns.SingleAsync();
            _ = run.ExecutionLeaseId.ShouldNotBeNull();
            runId = run.Id;
            run.ExecutionLeaseId = replacementLease;
            run.Status = status;
            run.CompletedAtUtc =
                status == AutomationFlowRunStatus.Completed
                    ? f.Clock.GetUtcNow().UtcDateTime
                    : null;
            _ = await db.SaveChangesAsync();
        }
        finally
        {
            release.SetResult();
        }
        _ = (
            await operation.WaitAsync(TimeSpan.FromSeconds(5))
        ).ShouldBeOfType<AutomationManualRunOutcome.Dispatched>();
        changes.States.ShouldBe([AutomationFlowRunState.Running]);
        await using var read = await f.Database.CreateDbContextAsync();
        var retained = await read.AutomationFlowRuns.FindAsync(runId);
        retained!.ExecutionLeaseId.ShouldBe(replacementLease);
        retained.Status.ShouldBe(status);
        chat.Calls.ShouldBe(1);
    }

    private sealed class ReadingCommittedRunChanges(RuntimeFixture fixture)
        : IAutomationUiChangePublisher
    {
        internal List<AutomationFlowRunState> States { get; } = [];

        public Task SourceReadinessChangedAsync(AutomationHostId host) =>
            fixture.UiChanges.SourceReadinessChangedAsync(host);

        public Task RunLimitRejectedAsync(
            AutomationHostId host,
            AutomationFlowId flow,
            AutomationAdmissionRejectionReason reason
        ) => fixture.UiChanges.RunLimitRejectedAsync(host, flow, reason);

        public async Task RunChangedAsync(AutomationHostId host, AutomationRunId run)
        {
            var current = (
                await fixture.Queries.ReadStatusAsync(host, [run], CancellationToken.None)
            ).ShouldBeOfType<AutomationRunStatusOutcome.Available>();
            States.Add(current.Runs.Single().State);
            await fixture.UiChanges.RunChangedAsync(host, run);
        }

        public Task CountdownChangedAsync(AutomationHostId host, string name) =>
            fixture.UiChanges.CountdownChangedAsync(host, name);

        public Task CountdownsChangedAsync(AutomationHostId host) =>
            fixture.UiChanges.CountdownsChangedAsync(host);
    }

    private sealed class DashboardAdmissionSaveFailure : SaveChangesInterceptor
    {
        internal bool Armed { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData data,
            InterceptionResult<int> result,
            CancellationToken ct = default
        ) =>
            Armed
            && data.Context!.ChangeTracker.Entries<AutomationNodeRun>()
                .Any(e => e.State == EntityState.Added)
                ? ValueTask.FromException<InterceptionResult<int>>(
                    new DbUpdateException("Finite admission rollback")
                )
                : ValueTask.FromResult(result);
    }
}
