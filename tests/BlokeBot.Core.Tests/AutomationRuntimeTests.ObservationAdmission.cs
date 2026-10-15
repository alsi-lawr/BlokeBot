using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class AutomationRuntimeTests
{
    private const string _admissionPrivateMessage = "private-observation-admission-message";

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ObservationAdmission_ChatWriteFailureRollsBackSeenAndEveryRun_EligibleRetryExecutesOnce(
        bool cancel
    )
    {
        using var cancellation = new CancellationTokenSource();
        var cut = new AdmissionWriteCut();
        var sender = new AdmissionSender();
        await using var f = await RuntimeFixture.CreateAsync(
            executionChat: sender,
            databaseInterceptors: [cut]
        );
        var first = await SaveExpandedChatAsync(
            f,
            "chat-match",
            """{"kind":"FirstObserved","match":""}""",
            "first"
        );
        _ = await SaveExpandedChatAsync(
            f,
            "chat-match",
            """{"kind":"FirstObserved","match":""}""",
            "second"
        );
        var x = await WarmAdmissionChatAsync(f, f.Runtime);
        var message = AdmissionMessage(f);
        cut.Arm(first, cancellation: cancel ? cancellation : null);
        if (cancel)
        {
            _ = await Should.ThrowAsync<OperationCanceledException>(() =>
                x.ChatReceivedAsync(message, cancellation.Token)
            );
        }
        else
        {
            _ = await Should.ThrowAsync<AdmissionWriteFault>(() =>
                x.ChatReceivedAsync(message, default)
            );
        }
        cut.Fired.ShouldBe(1);
        cut.Staged.ShouldNotBeNull().Seen.ShouldBe(1);
        sender.Entries.ShouldBeEmpty();
        await using (var db = await f.Database.CreateDbContextAsync())
        {
            (await db.AutomationSeenViewers.CountAsync()).ShouldBe(0);
            (await db.AutomationFlowRuns.CountAsync()).ShouldBe(0);
            (await db.AutomationNodeRuns.CountAsync()).ShouldBe(0);
            (await db.AutomationTraceEvents.CountAsync()).ShouldBe(0);
        }
        var freshRuntime = f.NewRuntime();
        await freshRuntime.InitializeAsync(default);
        var fresh = await WarmAdmissionChatAsync(f, freshRuntime);
        await fresh.ChatReceivedAsync(message, default);
        await x.ChatReceivedAsync(message, default);
        await fresh.ChatReceivedAsync(
            message with
            {
                MessageId = "same-viewer-another-message",
            },
            default
        );
        await freshRuntime.ResumeDueAsync(default);
        sender.Accepted.Order().ShouldBe(["first", "second"]);
        sender.Entries.Count.ShouldBe(2);
        await AssertAdmissionCompletedAsync(f, 2);
        await using var final = await f.Database.CreateDbContextAsync();
        (await final.AutomationSeenViewers.CountAsync()).ShouldBe(1);
        foreach (var run in await final.AutomationFlowRuns.ToArrayAsync())
        {
            var restored = AutomationRuntimeSerialization
                .RestoreContext(run.ContextSchemaVersion, run.ContextJson)
                .ShouldBeOfType<AutomationContextRestoreOutcome.Available>()
                .Context;
            var messageVariable = restored.Variables.ForExecution()[new("message-text")];
            messageVariable.Value.ShouldBe(new AutomationValue.Text(_admissionPrivateMessage));
            messageVariable.Sensitivity.ShouldBe(AutomationDataSensitivity.Sensitive);
            restored.Variables.SafeForExternalUse().ShouldNotContainKey(new("message-text"));
        }
        string.Join(
                "",
                await final.AutomationTraceEvents.Select(trace => trace.EventJson).ToArrayAsync()
            )
            .ShouldNotContain(_admissionPrivateMessage);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ObservationAdmission_MilestoneWriteFailureRollsBackLifecycleAndAllCrossings_FreshEpochRemainsBaseline(
        bool freshEpoch
    )
    {
        var cut = new AdmissionWriteCut();
        var sender = new AdmissionSender();
        await using var f = await RuntimeFixture.CreateAsync(
            executionChat: sender,
            databaseInterceptors: [cut]
        );
        _ = await SaveExpandedChatAsync(
            f,
            "twitch-goal",
            """{"event":"Progressed","milestone":10}""",
            "progress"
        );
        var milestone = await SaveExpandedChatAsync(
            f,
            "twitch-goal",
            """{"event":"Milestone","milestone":10}""",
            "ten"
        );
        _ = await SaveExpandedChatAsync(
            f,
            "twitch-goal",
            """{"event":"Milestone","milestone":20}""",
            "twenty"
        );
        await f.Runtime.InitializeAsync(default);
        var x = AdmissionExpandedFor(f, f.Runtime);
        await x.GoalChangedAsync(AdmissionGoal(f, EventSubGoalStage.Begin, 8), default);
        f.Clock.Advance(TimeSpan.FromSeconds(1));
        var crossing = AdmissionGoal(f, EventSubGoalStage.Progress, 20);
        cut.Arm(milestone);
        _ = await Should.ThrowAsync<AdmissionWriteFault>(() =>
            x.GoalChangedAsync(crossing, default)
        );
        cut.Fired.ShouldBe(1);
        cut.Staged.ShouldNotBeNull().GoalAmount.ShouldBe(20);
        cut.Staged.Milestones.ShouldBe(2);
        cut.Staged.RunFlows.ShouldContain(run => run != milestone.Value);
        sender.Entries.ShouldBeEmpty();
        await using (var db = await f.Database.CreateDbContextAsync())
        {
            (await db.AutomationGoalObservations.SingleAsync()).CurrentAmount.ShouldBe(8);
            (await db.AutomationGoalMilestones.CountAsync()).ShouldBe(0);
            (await db.AutomationFlowRuns.CountAsync()).ShouldBe(0);
            (await db.AutomationTraceEvents.CountAsync()).ShouldBe(0);
        }
        var recovery = freshEpoch ? f.NewRuntime() : f.Runtime;
        await recovery.InitializeAsync(default);
        var source = freshEpoch ? AdmissionExpandedFor(f, recovery) : x;
        await source.GoalChangedAsync(crossing, default);
        await source.GoalChangedAsync(crossing, default);
        await recovery.ResumeDueAsync(default);
        sender.Accepted.Order().ShouldBe(freshEpoch ? ["progress"] : ["progress", "ten", "twenty"]);
        await AssertAdmissionCompletedAsync(f, freshEpoch ? 1 : 3);
        await using var final = await f.Database.CreateDbContextAsync();
        (await final.AutomationGoalMilestones.CountAsync()).ShouldBe(freshEpoch ? 0 : 2);
        (await final.AutomationGoalObservations.SingleAsync()).CurrentAmount.ShouldBe(20);
    }

    [Test]
    public async Task ObservationAdmission_EndAdmissionFailureDoesNotConsumeEndedFence_FreshRetryCompletesOnce()
    {
        var cut = new AdmissionWriteCut();
        var sender = new AdmissionSender();
        await using var f = await RuntimeFixture.CreateAsync(
            executionChat: sender,
            databaseInterceptors: [cut]
        );
        var ended = await SaveExpandedChatAsync(
            f,
            "twitch-goal",
            """{"event":"Ended","milestone":10}""",
            "ended"
        );
        await f.Runtime.InitializeAsync(default);
        var x = AdmissionExpandedFor(f, f.Runtime);
        await x.GoalChangedAsync(AdmissionGoal(f, EventSubGoalStage.Begin, 8), default);
        f.Clock.Advance(TimeSpan.FromSeconds(1));
        var message = AdmissionGoal(f, EventSubGoalStage.End, 20);
        cut.Arm(ended);
        _ = await Should.ThrowAsync<AdmissionWriteFault>(() =>
            x.GoalChangedAsync(message, default)
        );
        cut.Staged.ShouldNotBeNull().GoalEnded.ShouldBeTrue();
        await using (var db = await f.Database.CreateDbContextAsync())
        {
            (await db.AutomationGoalObservations.SingleAsync()).IsEnded.ShouldBeFalse();
            (await db.AutomationFlowRuns.CountAsync()).ShouldBe(0);
        }
        sender.Entries.ShouldBeEmpty();
        var freshRuntime = f.NewRuntime();
        await freshRuntime.InitializeAsync(default);
        var fresh = AdmissionExpandedFor(f, freshRuntime);
        await fresh.GoalChangedAsync(message, default);
        await fresh.GoalChangedAsync(
            message with
            {
                MessageId = "later-end",
                Timestamp = message.Timestamp.AddSeconds(1),
            },
            default
        );
        sender.Accepted.ShouldBe(["ended"]);
        await AssertAdmissionCompletedAsync(f, 1);
        await using var final = await f.Database.CreateDbContextAsync();
        (await final.AutomationGoalObservations.SingleAsync()).IsEnded.ShouldBeTrue();
    }

    [Test]
    public async Task ObservationAdmission_CommittedGoalBatchSurvivesPreExecutionFailure_AllPendingWorkRecoversOnce()
    {
        var cut = new AdmissionWriteCut();
        var sender = new AdmissionSender();
        await using var f = await RuntimeFixture.CreateAsync(
            executionChat: sender,
            databaseInterceptors: [cut]
        );
        _ = await SaveExpandedChatAsync(
            f,
            "twitch-goal",
            """{"event":"Progressed","milestone":10}""",
            "progress"
        );
        var ten = await SaveExpandedChatAsync(
            f,
            "twitch-goal",
            """{"event":"Milestone","milestone":10}""",
            "ten"
        );
        _ = await SaveExpandedChatAsync(
            f,
            "twitch-goal",
            """{"event":"Milestone","milestone":20}""",
            "twenty"
        );
        await f.Runtime.InitializeAsync(default);
        var x = AdmissionExpandedFor(f, f.Runtime);
        await x.GoalChangedAsync(AdmissionGoal(f, EventSubGoalStage.Begin, 8), default);
        f.Clock.Advance(TimeSpan.FromSeconds(1));
        var crossing = AdmissionGoal(f, EventSubGoalStage.Progress, 20);
        cut.Arm(
            ten,
            afterCommit: async () =>
            {
                await using var db = await f.Database.CreateDbContextAsync();
                (await db.AutomationGoalMilestones.CountAsync()).ShouldBe(2);
                var admitted = await db
                    .AutomationFlowRuns.Include(run => run.NodeRuns)
                    .ToArrayAsync();
                admitted.Length.ShouldBe(3);
                admitted.ShouldAllBe(run =>
                    run.ExecutionLeaseId == null && run.Status == AutomationFlowRunStatus.Running
                );
                admitted.ShouldAllBe(run =>
                    run.NodeRuns.Count(node => node.Status == AutomationNodeRunStatus.Pending) == 1
                );
                admitted.ShouldAllBe(run =>
                    run.NodeRuns.Count(node => node.Status == AutomationNodeRunStatus.Succeeded)
                    == 1
                );
            }
        );
        _ = await Should.ThrowAsync<AdmissionWriteFault>(() =>
            x.GoalChangedAsync(crossing, default)
        );
        cut.Fired.ShouldBe(1);
        cut.Committed.ShouldBeTrue();
        sender.Entries.ShouldBeEmpty();
        var freshRuntime = f.NewRuntime();
        await freshRuntime.InitializeAsync(default);
        await freshRuntime.ResumeDueAsync(default);
        await freshRuntime.ResumeDueAsync(default);
        await x.GoalChangedAsync(crossing, default);
        var fresh = AdmissionExpandedFor(f, freshRuntime);
        await fresh.GoalChangedAsync(crossing, default);
        sender.Accepted.Order().ShouldBe(["progress", "ten", "twenty"]);
        sender.Entries.Count.ShouldBe(3);
        await AssertAdmissionCompletedAsync(f, 3);
    }

    [Test]
    public async Task ObservationAdmission_ConcurrentChatRedeliveryCannotDuplicateCommittedWork_ResourcesReleasedBeforeEffect()
    {
        var sender = new AdmissionSender();
        var cut = new AdmissionWriteCut();
        await using var f = await RuntimeFixture.CreateAsync(
            executionChat: sender,
            databaseInterceptors: [cut]
        );
        var first = await SaveExpandedChatAsync(
            f,
            "chat-match",
            """{"kind":"FirstObserved","match":""}""",
            "first"
        );
        var x = await WarmAdmissionChatAsync(f, f.Runtime);
        var message = AdmissionMessage(f);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        cut.Observe(first);
        sender.BeforeAccept = async () =>
        {
            cut.Committed.ShouldBeTrue();
            cut.Connection.ShouldNotBeNull().State.ShouldBe(ConnectionState.Closed);
            await using var db = await f.Database.CreateDbContextAsync();
            (await db.AutomationSeenViewers.CountAsync()).ShouldBe(1);
            (await db.AutomationFlowRuns.CountAsync()).ShouldBe(1);
            _ = entered.TrySetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(5));
        };
        var original = x.ChatReceivedAsync(message, default);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            sender.Accepted.ShouldBeEmpty();
            await x.ChatReceivedAsync(message, default).WaitAsync(TimeSpan.FromSeconds(5));
            sender.Entries.Count.ShouldBe(1);
        }
        finally
        {
            _ = release.TrySetResult();
        }
        await original.WaitAsync(TimeSpan.FromSeconds(5));
        await f.Runtime.ResumeDueAsync(default);
        sender.Accepted.ShouldBe(["first"]);
        await AssertAdmissionCompletedAsync(f, 1);
    }

    [Test]
    public async Task ObservationAdmission_GoalBatchIsFullyCommittedBeforeAnyEffect_ConcurrentRedeliveryDoesNotAdmitPrefixAgain()
    {
        var sender = new AdmissionSender();
        await using var f = await RuntimeFixture.CreateAsync(executionChat: sender);
        _ = await SaveExpandedChatAsync(
            f,
            "twitch-goal",
            """{"event":"Progressed","milestone":10}""",
            "progress"
        );
        _ = await SaveExpandedChatAsync(
            f,
            "twitch-goal",
            """{"event":"Milestone","milestone":10}""",
            "ten"
        );
        _ = await SaveExpandedChatAsync(
            f,
            "twitch-goal",
            """{"event":"Milestone","milestone":20}""",
            "twenty"
        );
        await f.Runtime.InitializeAsync(default);
        var x = AdmissionExpandedFor(f, f.Runtime);
        await x.GoalChangedAsync(AdmissionGoal(f, EventSubGoalStage.Begin, 8), default);
        f.Clock.Advance(TimeSpan.FromSeconds(1));
        var crossing = AdmissionGoal(f, EventSubGoalStage.Progress, 20);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sender.BeforeAccept = async () =>
        {
            await using var db = await f.Database.CreateDbContextAsync();
            (await db.AutomationFlowRuns.CountAsync()).ShouldBe(3);
            (await db.AutomationGoalMilestones.CountAsync()).ShouldBe(2);
            _ = entered.TrySetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(5));
        };
        var original = x.GoalChangedAsync(crossing, default);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await x.GoalChangedAsync(crossing, default).WaitAsync(TimeSpan.FromSeconds(5));
            sender.Entries.Count.ShouldBe(1);
            sender.Accepted.ShouldBeEmpty();
        }
        finally
        {
            _ = release.TrySetResult();
        }
        await original.WaitAsync(TimeSpan.FromSeconds(5));
        await f.Runtime.ResumeDueAsync(default);
        sender.Accepted.Order().ShouldBe(["progress", "ten", "twenty"]);
        await AssertAdmissionCompletedAsync(f, 3);
    }

    private static async Task AssertAdmissionCompletedAsync(RuntimeFixture f, int expected)
    {
        await using var db = await f.Database.CreateDbContextAsync();
        var runs = await db.AutomationFlowRuns.Include(run => run.NodeRuns).ToArrayAsync();
        runs.Length.ShouldBe(expected);
        runs.ShouldAllBe(run =>
            run.Status == AutomationFlowRunStatus.Completed && run.ExecutionLeaseId == null
        );
        runs.ShouldAllBe(run =>
            run.NodeRuns.All(node => node.Status == AutomationNodeRunStatus.Succeeded)
        );
        runs.Select(run => (run.FlowId, run.SourceNodeId, run.SourceOccurrenceId))
            .Distinct()
            .Count()
            .ShouldBe(expected);
    }

    private static EventSubObservedChatEvent AdmissionMessage(RuntimeFixture f) =>
        new(
            "first-admission-message",
            f.Clock.GetUtcNow(),
            "streamer-id",
            "streamer-id",
            "viewer-id",
            "viewer",
            "Viewer",
            _admissionPrivateMessage,
            []
        );

    private static EventSubGoalEvent AdmissionGoal(
        RuntimeFixture f,
        EventSubGoalStage stage,
        int amount
    ) =>
        new(
            stage.ToString(),
            f.Clock.GetUtcNow(),
            "streamer-id",
            "admission-goal",
            "follower",
            stage,
            amount,
            100,
            stage == EventSubGoalStage.End
        );

    private static ExpandedAutomationRuntime AdmissionExpandedFor(
        RuntimeFixture f,
        AutomationRuntimeService runtime,
        ExpandedStreams? streams = null
    ) =>
        new(
            f.Database,
            runtime,
            f.Countdowns,
            f.Clock,
            streams ?? new ExpandedStreams(),
            new ExpandedAccounts(),
            new ExpandedTokens(),
            new HelixClient(new ExpandedHttp(), TwitchEndpointPolicy.Default),
            BotSettings.FromOptions(new BotOptions()),
            new ExpandedConnection(),
            NullLogger<ExpandedAutomationRuntime>.Instance,
            uiChanges: f.UiChanges
        );

    private static async Task<ExpandedAutomationRuntime> WarmAdmissionChatAsync(
        RuntimeFixture f,
        AutomationRuntimeService runtime
    )
    {
        await runtime.InitializeAsync(default);
        var streams = new ExpandedStreams
        {
            Current = new HostStreamLivenessOutcome.Live(
                "admission-stream",
                f.Clock.GetUtcNow() - TimeSpan.FromMinutes(1)
            ),
        };
        var source = AdmissionExpandedFor(f, runtime, streams);
        await source.TickAsync(default);
        return source;
    }

    private sealed class AdmissionSender : IPublicChatMessageSender
    {
        internal ConcurrentQueue<string> Entries { get; } = [];
        internal ConcurrentQueue<string> Accepted { get; } = [];
        internal Func<Task>? BeforeAccept { get; set; }

        public async ValueTask<PublicChatSendOutcome> SendAsync(
            string channel,
            string message,
            PublicChatDeliveryDeadline deadline,
            CancellationToken cancellationToken
        )
        {
            Entries.Enqueue(message);
            if (BeforeAccept is { } before)
            {
                await before();
            }
            Accepted.Enqueue(message);
            return new PublicChatSendOutcome.Accepted();
        }
    }

    private sealed class AdmissionWriteFault : Exception;

    private sealed record AdmissionStagedState(
        int Seen,
        long? GoalAmount,
        bool GoalEnded,
        int Milestones,
        Guid[] RunFlows
    );

    private sealed class AdmissionWriteCut : DbCommandInterceptor, IDbTransactionInterceptor
    {
        private Guid? _flowId;
        private CancellationTokenSource? _cancellation;
        private Func<Task>? _afterCommit;
        private DbTransaction? _transaction;
        private bool _observeOnly;
        internal int Fired { get; private set; }
        internal bool Committed { get; private set; }
        internal AdmissionStagedState? Staged { get; private set; }
        internal DbConnection? Connection { get; private set; }

        internal void Arm(
            AutomationFlowId flow,
            CancellationTokenSource? cancellation = null,
            Func<Task>? afterCommit = null
        )
        {
            _flowId = flow.Value;
            _cancellation = cancellation;
            _afterCommit = afterCommit;
        }

        internal void Observe(AutomationFlowId flow)
        {
            _flowId = flow.Value;
            _observeOnly = true;
        }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            await BeforeWriteAsync(command, cancellationToken);
            return result;
        }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            await BeforeWriteAsync(command, cancellationToken);
            return result;
        }

        private async Task BeforeWriteAsync(DbCommand command, CancellationToken cancellationToken)
        {
            if (
                _flowId is not { } target
                || !command
                    .CommandText.TrimStart()
                    .StartsWith("INSERT", StringComparison.OrdinalIgnoreCase)
                || !command.CommandText.Contains(
                    "automation_flow_runs",
                    StringComparison.OrdinalIgnoreCase
                )
                || !command
                    .Parameters.Cast<DbParameter>()
                    .Any(parameter =>
                        Guid.TryParse(parameter.Value?.ToString(), out var value) && value == target
                    )
            )
            {
                return;
            }
            _flowId = null;
            Connection = command.Connection.ShouldNotBeNull();
            _transaction = command.Transaction.ShouldNotBeNull();
            await using var snapshot = Connection.CreateCommand();
            snapshot.Transaction = _transaction;
            snapshot.CommandText = """
                SELECT (SELECT COUNT(*) FROM automation_seen_viewers),
                       (SELECT CurrentAmount FROM automation_goal_observations LIMIT 1),
                       (SELECT IsEnded FROM automation_goal_observations LIMIT 1),
                       (SELECT COUNT(*) FROM automation_goal_milestones);
                """;
            await using (var reader = await snapshot.ExecuteReaderAsync(cancellationToken))
            {
                (await reader.ReadAsync(cancellationToken)).ShouldBeTrue();
                Staged = new(
                    reader.GetInt32(0),
                    reader.IsDBNull(1) ? null : reader.GetInt64(1),
                    !reader.IsDBNull(2) && reader.GetBoolean(2),
                    reader.GetInt32(3),
                    []
                );
            }
            snapshot.CommandText = "SELECT FlowId FROM automation_flow_runs;";
            var flows = new List<Guid>();
            await using (var reader = await snapshot.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    flows.Add(reader.GetGuid(0));
                }
            }
            Staged = Staged with { RunFlows = [.. flows] };
            if (_afterCommit is not null || _observeOnly)
            {
                return;
            }
            Fired++;
            if (_cancellation is not null)
            {
                await _cancellation.CancelAsync();
                cancellationToken.ThrowIfCancellationRequested();
            }
            throw new AdmissionWriteFault();
        }

        Task IDbTransactionInterceptor.TransactionCommittedAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken
        )
        {
            if (!ReferenceEquals(transaction, _transaction))
            {
                return Task.CompletedTask;
            }
            Committed = true;
            return _afterCommit is { } after ? FailAfterCommitAsync(after) : Task.CompletedTask;
        }

        private async Task FailAfterCommitAsync(Func<Task> after)
        {
            _afterCommit = null;
            Fired++;
            await after();
            throw new AdmissionWriteFault();
        }
    }
}
