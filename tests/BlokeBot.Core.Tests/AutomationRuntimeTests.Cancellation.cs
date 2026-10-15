using System.Collections.Immutable;
using System.Data.Common;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class AutomationRuntimeTests
{
    public enum ExecutionCancellationWindow
    {
        BeforeAdmission,
        AdmittedBeforeResult,
        AcceptedBeforeCheckpoint,
        CommittedSuccessBeforeCompletion,
    }

    [Test]
    [Arguments(ExecutionCancellationWindow.BeforeAdmission, false, false)]
    [Arguments(ExecutionCancellationWindow.BeforeAdmission, false, true)]
    [Arguments(ExecutionCancellationWindow.BeforeAdmission, true, false)]
    [Arguments(ExecutionCancellationWindow.BeforeAdmission, true, true)]
    [Arguments(ExecutionCancellationWindow.AdmittedBeforeResult, false, false)]
    [Arguments(ExecutionCancellationWindow.AdmittedBeforeResult, false, true)]
    [Arguments(ExecutionCancellationWindow.AdmittedBeforeResult, true, false)]
    [Arguments(ExecutionCancellationWindow.AdmittedBeforeResult, true, true)]
    [Arguments(ExecutionCancellationWindow.AcceptedBeforeCheckpoint, false, false)]
    [Arguments(ExecutionCancellationWindow.AcceptedBeforeCheckpoint, false, true)]
    [Arguments(ExecutionCancellationWindow.AcceptedBeforeCheckpoint, true, false)]
    [Arguments(ExecutionCancellationWindow.AcceptedBeforeCheckpoint, true, true)]
    [Arguments(ExecutionCancellationWindow.CommittedSuccessBeforeCompletion, false, false)]
    [Arguments(ExecutionCancellationWindow.CommittedSuccessBeforeCompletion, false, true)]
    [Arguments(ExecutionCancellationWindow.CommittedSuccessBeforeCompletion, true, false)]
    [Arguments(ExecutionCancellationWindow.CommittedSuccessBeforeCompletion, true, true)]
    public async Task CancelledExecution_ReconcilesOnlyOwnedInterruptedWorkWithoutReplay(
        ExecutionCancellationWindow window,
        bool continueOnFailure,
        bool directResume
    )
    {
        var barrier = new ExecutionCancellationBarrier();
        var checkpoint = new ExecutionCheckpointInterceptor(window, barrier);
        var sender = new ExecutionCancellationSender(window, barrier);
        await using var fixture = await RuntimeFixture.CreateAsync(
            executionChat: sender,
            databaseInterceptors: [checkpoint]
        );
        var runtime = fixture.Runtime;
        await runtime.InitializeAsync(CancellationToken.None);
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var action = Node(
            "send-chat",
            """{"message":"probe first"}""",
            continueOnFailure
                ? AutomationNodeFailurePolicy.Continue
                : AutomationNodeFailurePolicy.Stop
        );
        var continuation = Node("send-chat", """{"message":"probe continuation"}""");
        _ = await fixture.SaveAsync(
            [source, action, continuation],
            [Edge(source, "flow", action), Edge(action, "complete", continuation)]
        );
        sender.BeforeFirstAccepted = () =>
            checkpoint.Arm(async () =>
            {
                await using var db = await fixture.Database.CreateDbContextAsync();
                return await db
                    .AutomationNodeRuns.AsNoTracking()
                    .AnyAsync(node =>
                        node.NodeId == action.Id.Value
                        && node.Status == AutomationNodeRunStatus.Succeeded
                    );
            });
        using var cancellation = new CancellationTokenSource();
        var dispatch = runtime.DispatchAsync(
            new(Context(fixture.HostId), new CustomCommandSourceConfiguration(new(7))),
            cancellation.Token
        );
        try
        {
            await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var before = await ExecutionCancellationSnapshotAsync(fixture, sender);
            _ = before.Lease.ShouldNotBeNull();
            before.Status.ShouldBe(AutomationFlowRunStatus.Running);
            before.CompletedAtUtc.ShouldBeNull();
            var first = before.Nodes.Single(node => node.NodeId == action.Id.Value);
            if (window == ExecutionCancellationWindow.CommittedSuccessBeforeCompletion)
            {
                first.Status.ShouldBe(AutomationNodeRunStatus.Succeeded);
                _ = first.CompletedAtUtc.ShouldNotBeNull();
                _ = first.OutcomeCode.ShouldNotBeNull();
                before
                    .Nodes.Single(node => node.NodeId == continuation.Id.Value)
                    .Status.ShouldBe(AutomationNodeRunStatus.Pending);
                _ = checkpoint.CommittedTransactionId.ShouldNotBeNull();
                sender.Counts.ShouldBe(new(1, 1, 1));
            }
            else
            {
                if (window == ExecutionCancellationWindow.AcceptedBeforeCheckpoint)
                {
                    _ = checkpoint.PreCheckpointTransactionId.ShouldNotBeNull();
                }
                first.Status.ShouldBe(AutomationNodeRunStatus.Running);
                first.CompletedAtUtc.ShouldBeNull();
                first.OutcomeCode.ShouldBeNull();
                before.Nodes.Any(node => node.NodeId == continuation.Id.Value).ShouldBeFalse();
                sender.Counts.ShouldBe(
                    new(
                        1,
                        window == ExecutionCancellationWindow.BeforeAdmission ? 0 : 1,
                        window
                            is ExecutionCancellationWindow.BeforeAdmission
                                or ExecutionCancellationWindow.AdmittedBeforeResult
                            ? 0
                            : 1
                    )
                );
            }
            var runAtBarrier = new AutomationRunId(before.RunId!.Value);
            (await runtime.ResumeAsync(runAtBarrier, CancellationToken.None)).Status.ShouldBe(
                AutomationResumeStatus.Waiting
            );
            await runtime.ResumeDueAsync(CancellationToken.None);
            using (var competingCancellation = new CancellationTokenSource())
            {
                competingCancellation.Cancel();
                _ = await Should.ThrowAsync<OperationCanceledException>(() =>
                    runtime.ResumeAsync(runAtBarrier, competingCancellation.Token)
                );
            }
            var stillOwned = await ExecutionCancellationSnapshotAsync(fixture, sender);
            stillOwned.Lease.ShouldBe(before.Lease);
            stillOwned.Effects.ShouldBe(before.Effects);
            cancellation.Cancel();
            cancellation.Cancel();
            await barrier.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var during = await ExecutionCancellationSnapshotAsync(fixture, sender);
            during.Lease.ShouldBe(before.Lease);
            during.Effects.ShouldBe(before.Effects);
            during
                .Nodes.Single(node => node.NodeId == action.Id.Value)
                .Status.ShouldBe(first.Status);
            _ = barrier.ReleaseCancellation.TrySetResult();
            _ = await Should.ThrowAsync<OperationCanceledException>(() => dispatch);
            var reconciled = await ExecutionCancellationSnapshotAsync(fixture, sender);
            reconciled.Lease.ShouldBeNull();
            reconciled.Nodes.ShouldNotContain(node =>
                node.Status == AutomationNodeRunStatus.Running
                || node.Status == AutomationNodeRunStatus.Waiting
            );
            var committed = window == ExecutionCancellationWindow.CommittedSuccessBeforeCompletion;
            reconciled
                .Nodes.Single(node => node.NodeId == action.Id.Value)
                .Status.ShouldBe(
                    committed ? AutomationNodeRunStatus.Succeeded
                    : continueOnFailure ? AutomationNodeRunStatus.ContinuedAfterFailure
                    : AutomationNodeRunStatus.Failed
                );
            reconciled.Effects.ShouldBe(before.Effects);
            var runId = new AutomationRunId(reconciled.RunId!.Value);
            await runtime.InitializeAsync(CancellationToken.None);
            if (directResume)
            {
                var expected =
                    committed || continueOnFailure
                        ? AutomationResumeStatus.Completed
                        : AutomationResumeStatus.Failed;
                (await runtime.ResumeAsync(runId, CancellationToken.None)).Status.ShouldBe(
                    expected
                );
                (await runtime.ResumeAsync(runId, CancellationToken.None)).Status.ShouldBe(
                    expected
                );
            }
            else
            {
                await runtime.ResumeDueAsync(CancellationToken.None);
                fixture.Clock.Advance(TimeSpan.FromSeconds(1));
                await runtime.ResumeDueAsync(CancellationToken.None);
                fixture.Clock.Advance(TimeSpan.FromMinutes(1));
                await runtime.ResumeDueAsync(CancellationToken.None);
            }
            var final = await ExecutionCancellationSnapshotAsync(fixture, sender);
            final.Status.ShouldBe(
                committed || continueOnFailure
                    ? AutomationFlowRunStatus.Completed
                    : AutomationFlowRunStatus.Failed
            );
            final.Lease.ShouldBeNull();
            _ = final.CompletedAtUtc.ShouldNotBeNull();
            final.Nodes.ShouldNotContain(node =>
                node.Status == AutomationNodeRunStatus.Pending
                || node.Status == AutomationNodeRunStatus.Running
                || node.Status == AutomationNodeRunStatus.Waiting
            );
            var continuationRan = committed || continueOnFailure;
            final.Effects.ShouldBe(
                new(
                    before.Effects.Entries + (continuationRan ? 1 : 0),
                    before.Effects.Admissions + (continuationRan ? 1 : 0),
                    before.Effects.AcceptedResults + (continuationRan ? 1 : 0)
                )
            );
            var trace = await ReadTraceAsync(fixture, new(runId.Value));
            trace
                .Events.Where(item =>
                    item.Event.Kind == AutomationTraceEventKind.Attempt
                    && item.Event.Node?.Id == action.Id
                )
                .Count()
                .ShouldBe(1);
            trace.Events.Last().Event.Kind.ShouldBe(AutomationTraceEventKind.Terminal);
            trace
                .Events.Last()
                .Event.Outcome.ShouldBe(
                    continuationRan
                        ? AutomationTraceOutcome.Succeeded
                        : AutomationTraceOutcome.Failed
                );
        }
        finally
        {
            cancellation.Cancel();
            _ = barrier.ReleaseCancellation.TrySetResult();
        }
    }

    private static async Task<ExecutionCancellationState> ExecutionCancellationSnapshotAsync(
        RuntimeFixture fixture,
        ExecutionCancellationSender sender
    )
    {
        await using var db = await fixture.Database.CreateDbContextAsync();
        var run = await db.AutomationFlowRuns.AsNoTracking().SingleOrDefaultAsync();
        var nodes = await db
            .AutomationNodeRuns.AsNoTracking()
            .OrderBy(node => node.Sequence)
            .ToArrayAsync();
        return new(
            run?.Id,
            run?.Status,
            run?.ExecutionLeaseId,
            run?.CompletedAtUtc,
            [
                .. nodes.Select(node => new ExecutionCancellationNode(
                    node.NodeId,
                    node.Status,
                    node.CompletedAtUtc,
                    node.OutcomeCode
                )),
            ],
            sender.Counts
        );
    }

    private sealed record ExecutionCancellationState(
        Guid? RunId,
        AutomationFlowRunStatus? Status,
        Guid? Lease,
        DateTime? CompletedAtUtc,
        ImmutableArray<ExecutionCancellationNode> Nodes,
        ExecutionCancellationCounts Effects
    );

    private sealed record ExecutionCancellationNode(
        Guid NodeId,
        AutomationNodeRunStatus Status,
        DateTime? CompletedAtUtc,
        string? OutcomeCode
    );

    private sealed record ExecutionCancellationCounts(
        int Entries,
        int Admissions,
        int AcceptedResults
    );

    private sealed class ExecutionCancellationBarrier
    {
        internal TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource CancellationObserved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseCancellation { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal async Task PauseAsync(CancellationToken cancellationToken)
        {
            _ = Entered.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            finally
            {
                _ = CancellationObserved.TrySetResult();
                await ReleaseCancellation.Task;
            }
        }
    }

    private sealed class ExecutionCheckpointInterceptor(
        ExecutionCancellationWindow window,
        ExecutionCancellationBarrier barrier
    ) : DbTransactionInterceptor
    {
        private int _armed;
        private int _intercepted;
        private Func<Task<bool>> _committedSuccessVisible = static () => Task.FromResult(false);
        internal Guid? PreCheckpointTransactionId { get; private set; }
        internal Guid? CommittedTransactionId { get; private set; }

        internal void Arm(Func<Task<bool>> committedSuccessVisible)
        {
            _committedSuccessVisible = committedSuccessVisible;
            Volatile.Write(ref _armed, 1);
        }

        public override async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection,
            TransactionStartingEventData eventData,
            InterceptionResult<DbTransaction> result,
            CancellationToken cancellationToken = default
        )
        {
            if (
                window != ExecutionCancellationWindow.AcceptedBeforeCheckpoint
                || Volatile.Read(ref _armed) == 0
                || Interlocked.CompareExchange(ref _intercepted, 1, 0) != 0
            )
            {
                return result;
            }
            PreCheckpointTransactionId = eventData.TransactionId;
            await barrier.PauseAsync(cancellationToken);
            return result;
        }

        public override async Task TransactionCommittedAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default
        )
        {
            if (
                window != ExecutionCancellationWindow.CommittedSuccessBeforeCompletion
                || Volatile.Read(ref _armed) == 0
                || Volatile.Read(ref _intercepted) != 0
                || !await _committedSuccessVisible()
                || Interlocked.CompareExchange(ref _intercepted, 1, 0) != 0
            )
            {
                return;
            }
            CommittedTransactionId = eventData.TransactionId;
            await barrier.PauseAsync(cancellationToken);
        }
    }

    private sealed class ExecutionCancellationSender(
        ExecutionCancellationWindow window,
        ExecutionCancellationBarrier barrier
    ) : IPublicChatMessageSender
    {
        private int _entries;
        private int _admissions;
        private int _acceptedResults;
        internal Action BeforeFirstAccepted { get; set; } = static () => { };
        internal ExecutionCancellationCounts Counts =>
            new(
                Volatile.Read(ref _entries),
                Volatile.Read(ref _admissions),
                Volatile.Read(ref _acceptedResults)
            );

        public async ValueTask<PublicChatSendOutcome> SendAsync(
            string channel,
            string message,
            PublicChatDeliveryDeadline deadline,
            CancellationToken cancellationToken
        )
        {
            var entry = Interlocked.Increment(ref _entries);
            if (entry == 1 && window == ExecutionCancellationWindow.BeforeAdmission)
            {
                await barrier.PauseAsync(cancellationToken);
            }
            _ = Interlocked.Increment(ref _admissions);
            if (entry == 1)
            {
                if (window == ExecutionCancellationWindow.AdmittedBeforeResult)
                {
                    await barrier.PauseAsync(cancellationToken);
                }
                BeforeFirstAccepted();
            }
            _ = Interlocked.Increment(ref _acceptedResults);
            return new PublicChatSendOutcome.Accepted();
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CancelledClaim_BeforeOrAfterCommitReleasesOnlyItsLeaseAndKeepsUnstartedWork(
        bool afterCommit
    )
    {
        var barrier = new ExecutionCancellationBarrier();
        var interceptor = new ClaimCancellationInterceptor(afterCommit, barrier);
        await using var fixture = await RuntimeFixture.CreateAsync(
            databaseInterceptors: [interceptor]
        );
        var runtime = fixture.Runtime;
        await runtime.InitializeAsync(CancellationToken.None);
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var delay = Node("delay", """{"duration-milliseconds":1000}""");
        var action = Node("send-chat", """{"message":"after claim"}""");
        _ = await fixture.SaveAsync(
            [source, delay, action],
            [Edge(source, "flow", delay), Edge(delay, "complete", action)]
        );
        var runId = (
            await runtime.DispatchAsync(
                new(Context(fixture.HostId), new CustomCommandSourceConfiguration(new(7))),
                CancellationToken.None
            )
        ).RunIds.ShouldHaveSingleItem();
        interceptor.Arm(async () =>
        {
            await using var db = await fixture.Database.CreateDbContextAsync();
            return await db.AutomationFlowRuns.AnyAsync(run =>
                run.Id == runId.Value && run.ExecutionLeaseId != null
            );
        });
        using var cancellation = new CancellationTokenSource();
        var resume = runtime.ResumeAsync(runId, cancellation.Token);
        try
        {
            await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await using (var db = await fixture.Database.CreateDbContextAsync())
            {
                var run = await db.AutomationFlowRuns.Include(run => run.NodeRuns).SingleAsync();
                (run.ExecutionLeaseId != null).ShouldBe(afterCommit);
                run.NodeRuns.Single(node => node.NodeId == action.Id.Value)
                    .Status.ShouldBe(AutomationNodeRunStatus.Pending);
                run.NodeRuns.ShouldNotContain(node =>
                    node.Status == AutomationNodeRunStatus.Running
                );
            }
            cancellation.Cancel();
            await barrier.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(10));
            _ = barrier.ReleaseCancellation.TrySetResult();
            _ = await Should.ThrowAsync<OperationCanceledException>(() => resume);
            await using (var db = await fixture.Database.CreateDbContextAsync())
            {
                var run = await db.AutomationFlowRuns.Include(run => run.NodeRuns).SingleAsync();
                run.ExecutionLeaseId.ShouldBeNull();
                run.Status.ShouldBe(AutomationFlowRunStatus.Waiting);
                run.NodeRuns.Single(node => node.NodeId == action.Id.Value)
                    .Status.ShouldBe(AutomationNodeRunStatus.Pending);
            }
            await runtime.ResumeDueAsync(CancellationToken.None);
            fixture.Chat.Calls.ShouldBe(0);
            fixture.Clock.Advance(TimeSpan.FromSeconds(1));
            await runtime.ResumeDueAsync(CancellationToken.None);
            await runtime.ResumeDueAsync(CancellationToken.None);
            fixture.Chat.Messages.ShouldBe(["after claim"]);
            (await runtime.ResumeAsync(runId, CancellationToken.None)).Status.ShouldBe(
                AutomationResumeStatus.Completed
            );
        }
        finally
        {
            cancellation.Cancel();
            _ = barrier.ReleaseCancellation.TrySetResult();
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task InterruptedDurableImage_FreshRuntimeRecoversStopOrContinueWithoutReplaying(
        bool continues
    )
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstChat = InterruptFirstSend(entered);
        await using var live = await RuntimeFixture.CreateAsync(chat: firstChat);
        await live.Runtime.InitializeAsync(CancellationToken.None);
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var action = Node(
            "send-chat",
            """{"message":"uncertain"}""",
            continues ? AutomationNodeFailurePolicy.Continue : AutomationNodeFailurePolicy.Stop
        );
        var after = Node("send-chat", """{"message":"after restart"}""");
        _ = await live.SaveAsync(
            [source, action, after],
            [Edge(source, "flow", action), Edge(action, "complete", after)]
        );
        using var cancellation = new CancellationTokenSource();
        var dispatch = live.Runtime.DispatchAsync(
            new(Context(live.HostId), new CustomCommandSourceConfiguration(new(7))),
            cancellation.Token
        );
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await using var restarted = await RuntimeFixture.CreateAsync();
        try
        {
            await using (var fromDb = await live.Database.CreateDbContextAsync())
            await using (var toDb = await restarted.Database.CreateDbContextAsync())
            {
                var from = (Microsoft.Data.Sqlite.SqliteConnection)
                    fromDb.Database.GetDbConnection();
                var to = (Microsoft.Data.Sqlite.SqliteConnection)toDb.Database.GetDbConnection();
                await from.OpenAsync();
                await to.OpenAsync();
                from.BackupDatabase(to);
            }
            var runId = new AutomationRunId(await SingleRunIdAsync(restarted));
            await using (var before = await restarted.Database.CreateDbContextAsync())
            {
                var run = await before
                    .AutomationFlowRuns.Include(run => run.NodeRuns)
                    .SingleAsync();
                _ = run.ExecutionLeaseId.ShouldNotBeNull();
                run.NodeRuns.Single(node => node.NodeId == action.Id.Value)
                    .Status.ShouldBe(AutomationNodeRunStatus.Running);
            }
            cancellation.Cancel();
            _ = await Should.ThrowAsync<OperationCanceledException>(() => dispatch);
            await restarted.Runtime.InitializeAsync(CancellationToken.None);
            (await restarted.Runtime.ResumeAsync(runId, CancellationToken.None)).Status.ShouldBe(
                continues ? AutomationResumeStatus.Completed : AutomationResumeStatus.Failed
            );
            (await restarted.Runtime.ResumeAsync(runId, CancellationToken.None)).Status.ShouldBe(
                continues ? AutomationResumeStatus.Completed : AutomationResumeStatus.Failed
            );
            restarted.Chat.Messages.ShouldBe(continues ? ["after restart"] : []);
            firstChat.Messages.ShouldBe(["uncertain"]);
            await using var verified = await restarted.Database.CreateDbContextAsync();
            var settled = await verified
                .AutomationFlowRuns.Include(run => run.NodeRuns)
                .SingleAsync();
            settled.ExecutionLeaseId.ShouldBeNull();
            settled.NodeRuns.ShouldNotContain(node =>
                node.Status == AutomationNodeRunStatus.Running
                || node.Status == AutomationNodeRunStatus.Pending
                || node.Status == AutomationNodeRunStatus.Waiting
            );
            settled
                .NodeRuns.Single(node => node.NodeId == action.Id.Value)
                .Status.ShouldBe(
                    continues
                        ? AutomationNodeRunStatus.ContinuedAfterFailure
                        : AutomationNodeRunStatus.Failed
                );
        }
        finally
        {
            cancellation.Cancel();
        }
    }

    [Test]
    public async Task RepeatedCancellation_OldFinallyCannotReleaseSuccessorLeaseOrReplayEitherEffect()
    {
        var sender = new RepeatedCancellationSender();
        var cleanup = new RecoveryCommitBarrier();
        await using var fixture = await RuntimeFixture.CreateAsync(
            executionChat: sender,
            databaseInterceptors: [cleanup]
        );
        await fixture.Runtime.InitializeAsync(CancellationToken.None);
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var first = Node(
            "send-chat",
            """{"message":"first"}""",
            AutomationNodeFailurePolicy.Continue
        );
        var second = Node(
            "send-chat",
            """{"message":"second"}""",
            AutomationNodeFailurePolicy.Continue
        );
        var last = Node("send-chat", """{"message":"last"}""");
        _ = await fixture.SaveAsync(
            [source, first, second, last],
            [
                Edge(source, "flow", first),
                Edge(first, "complete", second),
                Edge(second, "complete", last),
            ]
        );
        using var firstCancellation = new CancellationTokenSource();
        using var secondCancellation = new CancellationTokenSource();
        var initial = fixture.Runtime.DispatchAsync(
            new(Context(fixture.HostId), new CustomCommandSourceConfiguration(new(7))),
            firstCancellation.Token
        );
        Task<AutomationResumeOutcome>? successor = null;
        try
        {
            await sender.FirstEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var runId = new AutomationRunId(await SingleRunIdAsync(fixture));
            Guid? firstLease;
            await using (var db = await fixture.Database.CreateDbContextAsync())
            {
                firstLease = (await db.AutomationFlowRuns.SingleAsync()).ExecutionLeaseId;
            }
            _ = firstLease.ShouldNotBeNull();
            cleanup.Arm(async () =>
            {
                await using var db = await fixture.Database.CreateDbContextAsync();
                return await db.AutomationFlowRuns.AnyAsync(run => run.ExecutionLeaseId == null)
                    && await db.AutomationNodeRuns.AnyAsync(node =>
                        node.NodeId == first.Id.Value
                        && node.Status == AutomationNodeRunStatus.ContinuedAfterFailure
                    );
            });
            firstCancellation.Cancel();
            await cleanup.Committed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            successor = fixture.Runtime.ResumeAsync(runId, secondCancellation.Token);
            await sender.SecondEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Guid? successorLease;
            await using (var db = await fixture.Database.CreateDbContextAsync())
            {
                successorLease = (await db.AutomationFlowRuns.SingleAsync()).ExecutionLeaseId;
            }
            _ = successorLease.ShouldNotBeNull();
            successorLease.ShouldNotBe(firstLease);
            _ = cleanup.Release.TrySetResult();
            _ = await Should.ThrowAsync<OperationCanceledException>(() => initial);
            await using (var db = await fixture.Database.CreateDbContextAsync())
            {
                (await db.AutomationFlowRuns.SingleAsync()).ExecutionLeaseId.ShouldBe(
                    successorLease
                );
                (
                    await db.AutomationNodeRuns.SingleAsync(node => node.NodeId == second.Id.Value)
                ).Status.ShouldBe(AutomationNodeRunStatus.Running);
            }
            (await fixture.Runtime.ResumeAsync(runId, CancellationToken.None)).Status.ShouldBe(
                AutomationResumeStatus.Waiting
            );
            secondCancellation.Cancel();
            secondCancellation.Cancel();
            _ = await Should.ThrowAsync<OperationCanceledException>(() => successor);
            await fixture.Runtime.ResumeDueAsync(CancellationToken.None);
            await fixture.Runtime.ResumeDueAsync(CancellationToken.None);
            (await fixture.Runtime.ResumeAsync(runId, CancellationToken.None)).Status.ShouldBe(
                AutomationResumeStatus.Completed
            );
            sender.Entries.ShouldBe(3);
            await using var final = await fixture.Database.CreateDbContextAsync();
            var run = await final.AutomationFlowRuns.Include(run => run.NodeRuns).SingleAsync();
            run.ExecutionLeaseId.ShouldBeNull();
            run.NodeRuns.Single(node => node.NodeId == first.Id.Value)
                .Status.ShouldBe(AutomationNodeRunStatus.ContinuedAfterFailure);
            run.NodeRuns.Single(node => node.NodeId == second.Id.Value)
                .Status.ShouldBe(AutomationNodeRunStatus.ContinuedAfterFailure);
            run.NodeRuns.Single(node => node.NodeId == last.Id.Value)
                .Status.ShouldBe(AutomationNodeRunStatus.Succeeded);
        }
        finally
        {
            firstCancellation.Cancel();
            secondCancellation.Cancel();
            _ = cleanup.Release.TrySetResult();
        }
    }

    private sealed class ClaimCancellationInterceptor(
        bool afterCommit,
        ExecutionCancellationBarrier barrier
    ) : DbTransactionInterceptor
    {
        private int _armed;
        private int _intercepted;
        private Func<Task<bool>> _owned = static () => Task.FromResult(false);

        internal void Arm(Func<Task<bool>> owned)
        {
            _owned = owned;
            Volatile.Write(ref _armed, 1);
        }

        public override async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection,
            TransactionStartingEventData eventData,
            InterceptionResult<DbTransaction> result,
            CancellationToken cancellationToken = default
        )
        {
            if (
                !afterCommit
                && Volatile.Read(ref _armed) != 0
                && Interlocked.CompareExchange(ref _intercepted, 1, 0) == 0
            )
            {
                await barrier.PauseAsync(cancellationToken);
            }
            return result;
        }

        public override async Task TransactionCommittedAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default
        )
        {
            if (
                afterCommit
                && Volatile.Read(ref _armed) != 0
                && Volatile.Read(ref _intercepted) == 0
                && await _owned()
                && Interlocked.CompareExchange(ref _intercepted, 1, 0) == 0
            )
            {
                await barrier.PauseAsync(cancellationToken);
            }
        }
    }

    private sealed class RecoveryCommitBarrier : DbTransactionInterceptor
    {
        private Func<Task<bool>> _committed = static () => Task.FromResult(false);
        private int _armed;
        private int _intercepted;
        internal TaskCompletionSource Committed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal void Arm(Func<Task<bool>> committed)
        {
            _committed = committed;
            Volatile.Write(ref _armed, 1);
        }

        public override async Task TransactionCommittedAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default
        )
        {
            if (
                Volatile.Read(ref _armed) != 0
                && Volatile.Read(ref _intercepted) == 0
                && await _committed()
                && Interlocked.CompareExchange(ref _intercepted, 1, 0) == 0
            )
            {
                _ = Committed.TrySetResult();
                await Release.Task;
            }
        }
    }

    private sealed class RepeatedCancellationSender : IPublicChatMessageSender
    {
        private int _entries;
        internal int Entries => Volatile.Read(ref _entries);
        internal TaskCompletionSource FirstEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource SecondEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<PublicChatSendOutcome> SendAsync(
            string channel,
            string message,
            PublicChatDeliveryDeadline deadline,
            CancellationToken cancellationToken
        )
        {
            var call = Interlocked.Increment(ref _entries);
            if (call <= 2)
            {
                _ = (call == 1 ? FirstEntered : SecondEntered).TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            return new PublicChatSendOutcome.Accepted();
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CancelledNestedEffect_SameRuntimeUnwindsWithoutReplayingCheckpointOrEffect(
        bool callerContinues
    )
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var chat = InterruptFirstSend(entered);
        var handler = TextValueHandler("test-counting-text-value", "single-inner-attempt");
        await using var fixture = await RuntimeFixture.CreateAsync(chat: chat, handlers: [handler]);
        var draft = Subflow(fixture.HostId);
        var value = Node("test-counting-text-value", "{}");
        var action = Node(
            "test-text-consumer",
            """{"message":"fallback"}""",
            bindings: Bindings("message", AutomationInputBindingMode.Connected)
        );
        draft = draft with
        {
            Graph = draft.Graph with
            {
                Nodes = [draft.Graph.Nodes[0], value, action, draft.Graph.Nodes[1]],
                Edges =
                [
                    Edge(draft.Graph.Nodes[0], "complete", action),
                    Edge(action, "complete", draft.Graph.Nodes[1]),
                    Edge(value, "value", action, "message", AutomationEdgeKind.Data),
                ],
            },
        };
        var revision = await Publish(Subflows(fixture), draft);
        var caller = Caller(fixture.HostId, revision);
        var invoke = caller.Nodes[1] with
        {
            FailurePolicy = callerContinues
                ? AutomationNodeFailurePolicy.Continue
                : AutomationNodeFailurePolicy.Stop,
        };
        var after = Node("send-chat", """{"message":"after"}""");
        caller = caller with
        {
            Nodes = [caller.Nodes[0], invoke, after],
            Edges = [Edge(caller.Nodes[0], "flow", invoke), Edge(invoke, "complete", after)],
        };
        _ = (
            await fixture.Flows.SaveAsync(caller, CancellationToken.None)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        using var cancellation = new CancellationTokenSource();
        var dispatch = fixture.Runtime.DispatchAsync(
            new(Context(fixture.HostId), new CustomCommandSourceConfiguration(new(7))),
            cancellation.Token
        );
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var runId = await SingleRunIdAsync(fixture);
        cancellation.Cancel();
        _ = await Should.ThrowAsync<OperationCanceledException>(() => dispatch);
        (await fixture.Runtime.ResumeAsync(new(runId), CancellationToken.None)).Status.ShouldBe(
            callerContinues ? AutomationResumeStatus.Completed : AutomationResumeStatus.Failed
        );
        handler.Calls.ShouldBe(1);
        chat.Messages.ShouldBe(
            callerContinues ? ["single-inner-attempt", "after"] : ["single-inner-attempt"]
        );
        await using var db = await fixture.Database.CreateDbContextAsync();
        (await db.AutomationSubflowRunReferences.CountAsync()).ShouldBe(0);
        (
            await db.AutomationNodeRuns.AnyAsync(node =>
                node.Status == AutomationNodeRunStatus.Pending
                || node.Status == AutomationNodeRunStatus.Running
                || node.Status == AutomationNodeRunStatus.Waiting
            )
        ).ShouldBeFalse();
        var trace = await ReadTraceAsync(fixture, new(runId));
        trace
            .Events.Count(item => item.Event.Kind == AutomationTraceEventKind.SubflowExit)
            .ShouldBe(1);
    }

    [Test]
    public async Task CancelledLazyProducer_SettlesBothRunningCheckpointsWithoutReevaluationOrEffect()
    {
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new CancelledPureProducer(
            TextValueHandler("test-counting-text-value", "unused"),
            entered
        );
        await using var fixture = await RuntimeFixture.CreateAsync(handlers: [handler]);
        await fixture.Runtime.InitializeAsync(CancellationToken.None);
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var value = Node("test-counting-text-value", "{}");
        var action = Node(
            "test-text-consumer",
            """{"message":"fallback"}""",
            bindings: Bindings("message", AutomationInputBindingMode.Connected)
        );
        _ = await fixture.SaveAsync(
            [source, value, action],
            [
                Edge(source, "flow", action),
                Edge(value, "value", action, "message", AutomationEdgeKind.Data),
            ]
        );
        var dispatch = fixture.Runtime.DispatchAsync(
            new(Context(fixture.HostId), new CustomCommandSourceConfiguration(new(7))),
            cancellation.Token
        );
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var runId = new AutomationRunId(await SingleRunIdAsync(fixture));
            await using (var db = await fixture.Database.CreateDbContextAsync())
            {
                var run = await db.AutomationFlowRuns.Include(run => run.NodeRuns).SingleAsync();
                run.NodeRuns.Single(node => node.NodeId == value.Id.Value)
                    .Status.ShouldBe(AutomationNodeRunStatus.Running);
                run.NodeRuns.Single(node => node.NodeId == action.Id.Value)
                    .Status.ShouldBe(AutomationNodeRunStatus.Running);
            }
            cancellation.Cancel();
            _ = await Should.ThrowAsync<OperationCanceledException>(() => dispatch);
            await fixture.Runtime.ResumeDueAsync(CancellationToken.None);
            (await fixture.Runtime.ResumeAsync(runId, CancellationToken.None)).Status.ShouldBe(
                AutomationResumeStatus.Failed
            );
            (await fixture.NewRuntime().ResumeAsync(runId, CancellationToken.None)).Status.ShouldBe(
                AutomationResumeStatus.Failed
            );
            handler.Calls.ShouldBe(1);
            fixture.Chat.Calls.ShouldBe(0);
            await using var final = await fixture.Database.CreateDbContextAsync();
            var nodes = await final.AutomationNodeRuns.ToArrayAsync();
            nodes
                .Single(node => node.NodeId == value.Id.Value)
                .Status.ShouldBe(AutomationNodeRunStatus.Failed);
            nodes.Single(node => node.NodeId == value.Id.Value).OutputJson.ShouldBeNull();
            nodes
                .Single(node => node.NodeId == action.Id.Value)
                .Status.ShouldBe(AutomationNodeRunStatus.Failed);
        }
        finally
        {
            cancellation.Cancel();
        }
    }

    private sealed class CancelledPureProducer(
        IAutomationPureNodeHandler inner,
        TaskCompletionSource entered
    ) : IAutomationPureNodeHandler
    {
        private int _calls;
        internal int Calls => Volatile.Read(ref _calls);
        public AutomationPureHandlerContract Contract => inner.Contract;

        public async ValueTask<AutomationPureNodeResult> ExecuteAsync(
            AutomationPureNodeInput input,
            CancellationToken cancellationToken
        )
        {
            _ = Interlocked.Increment(ref _calls);
            _ = entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return await inner.ExecuteAsync(input, cancellationToken);
        }
    }
}
