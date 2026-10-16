using System.Data.Common;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class AutomationRuntimeTests
{
    [Test, Explicit]
    [Arguments(false)]
    [Arguments(true)]
    public async Task UserCancellation_PostgreSqlDispatchAdmissionAndCancellationSerializeAtRunRow(
        bool admissionWins
    )
    {
        var barrier = new UserCancellationAdmissionBarrier(admissionWins);
        var provider = await PointProviderFixture.PostgreSqlAsync(barrier);
        var effectEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var effectRelease = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var sender = new RecordingChatSender(
            null,
            async (_, _, token) =>
            {
                _ = effectEntered.TrySetResult();
                await effectRelease.Task.WaitAsync(token);
            }
        );
        await using var f = await RuntimeFixture.CreateAsync(provider: provider, chat: sender);
        var source = Node("custom-command", """{"custom-command-id":7}""");
        var delay = Node("delay", """{"duration-milliseconds":1000}""");
        var send = Node("send-chat", """{"message":"admitted"}""");
        var later = Node("send-chat", """{"message":"must not invoke"}""");
        _ = await f.SaveAsync(
            [source, delay, send, later],
            [
                Edge(source, "flow", delay),
                Edge(delay, "complete", send),
                Edge(send, "complete", later),
            ]
        );
        var id = (
            await f.Runtime.DispatchAsync(
                new(Context(f.HostId), new CustomCommandSourceConfiguration(new(7))),
                default
            )
        ).RunIds.ShouldHaveSingleItem();
        f.Clock.Advance(TimeSpan.FromSeconds(2));
        barrier.Arm();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var resume = f.Runtime.ResumeAsync(id, timeout.Token);
        try
        {
            var leader = await barrier.Entered.Task.WaitAsync(timeout.Token);
            var cancel = f.Runtime.CancelRunAsync(new(f.HostId), id, timeout.Token);
            if (admissionWins)
            {
                var contender = await barrier.CancellationAttempted.Task.WaitAsync(timeout.Token);
                await using var db = await provider.CreateDbContextAsync(timeout.Token);
                await using var connection = new NpgsqlConnection(
                    db.Database.GetConnectionString()
                );
                await connection.OpenAsync(timeout.Token);
                await using var probe = new NpgsqlCommand(
                    "SELECT @leader = ANY(pg_blocking_pids(@contender))",
                    connection
                );
                _ = probe.Parameters.AddWithValue("leader", leader);
                _ = probe.Parameters.AddWithValue("contender", contender);
                var blocked = false;
                while (!blocked)
                {
                    blocked = (bool)(await probe.ExecuteScalarAsync(timeout.Token))!;
                    if (!blocked)
                    {
                        await Task.Delay(20, timeout.Token);
                    }
                }
                Console.WriteLine(
                    $"358 persisted dispatch-admission fence: leader={leader}, cancellation={contender}, pg_blocking_pids=true"
                );
                cancel.IsCompleted.ShouldBeFalse();
            }
            else
            {
                (await cancel).ShouldBe(1);
                sender.Calls.ShouldBe(0);
            }
            _ = barrier.Release.TrySetResult();
            if (admissionWins)
            {
                await effectEntered.Task.WaitAsync(timeout.Token);
                (await cancel).ShouldBe(1);
            }
            _ = effectRelease.TrySetResult();
            (await resume).Status.ShouldBe(AutomationResumeStatus.Cancelled);
            sender.Calls.ShouldBe(admissionWins ? 1 : 0);
            await f.NewRuntime().ResumeDueAsync(timeout.Token);
            sender.Calls.ShouldBe(admissionWins ? 1 : 0);
            await using var verify = await provider.CreateDbContextAsync(timeout.Token);
            (await verify.AutomationFlowRuns.SingleAsync()).Status.ShouldBe(
                AutomationFlowRunStatus.Cancelled
            );
            (
                await verify
                    .AutomationNodeRuns.Where(node => node.NodeId == send.Id.Value)
                    .SingleAsync()
            ).Status.ShouldBe(AutomationNodeRunStatus.Cancelled);
        }
        finally
        {
            _ = barrier.Release.TrySetResult();
            _ = effectRelease.TrySetResult();
            _ = await resume;
        }
    }

    [Test, Explicit]
    [Arguments(false)]
    [Arguments(true)]
    public async Task UserCancellation_PostgreSqlStaleRecoveryOrPluginSnapshotCannotReplaceTerminal(
        bool pluginInvalidation
    )
    {
        var barrier = new UserCancellationSnapshotBarrier();
        var provider = await PointProviderFixture.PostgreSqlAsync(barrier);
        await using var f = await RuntimeFixture.CreateAsync(provider: provider);
        var plugin = await SubflowPlugin(f);
        var leaf = Subflow(f.HostId);
        var delay = Node("delay", """{"duration-milliseconds":1000}""");
        leaf = leaf with
        {
            Graph = leaf.Graph with
            {
                Nodes = [leaf.Graph.Nodes[0], delay, plugin.Node, leaf.Graph.Nodes[1]],
                Edges =
                [
                    Edge(leaf.Graph.Nodes[0], "complete", delay),
                    Edge(delay, "complete", plugin.Node),
                    Edge(plugin.Node, "complete", leaf.Graph.Nodes[1]),
                ],
            },
        };
        var library = new AutomationSubflowService(f.Database, plugin.Flows, f.Clock);
        var published = await Publish(library, leaf);
        _ = (
            await plugin.Flows.SaveAsync(Caller(f.HostId, published), default)
        ).ShouldBeOfType<AutomationFlowSaveOutcome.Saved>();
        var id = (
            await plugin.Runtime.DispatchAsync(
                new(Context(f.HostId), new CustomCommandSourceConfiguration(new(7))),
                default
            )
        ).RunIds.ShouldHaveSingleItem();
        barrier.Arm();
        var stale = pluginInvalidation
            ? new PluginAutomationRunCoordinator(f.Database, f.Clock, f.UiChanges).CancelAsync(
                plugin.State,
                default
            )
            : f.NewRuntime().InitializeAsync(default);
        try
        {
            await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            (await f.Runtime.CancelRunAsync(new(f.HostId), id, default)).ShouldBe(1);
        }
        finally
        {
            _ = barrier.Release.TrySetResult();
        }
        await stale;
        f.Clock.Advance(TimeSpan.FromSeconds(2));
        (await plugin.Runtime.ResumeAsync(id, default)).Status.ShouldBe(
            AutomationResumeStatus.Cancelled
        );
        plugin.Invoker.Calls.ShouldBe(0);
        f.Chat.Calls.ShouldBe(0);
        await using var db = await f.Database.CreateDbContextAsync();
        (await db.AutomationFlowRuns.SingleAsync()).Status.ShouldBe(
            AutomationFlowRunStatus.Cancelled
        );
        (await db.AutomationSubflowRunReferences.CountAsync()).ShouldBe(0);
        (await ReadTraceAsync(f, new(id.Value))).OutcomeSummary!.TerminalOutcome.ShouldBe(
            AutomationTraceOutcome.Cancelled
        );
    }

    private sealed class UserCancellationSnapshotBarrier : DbCommandInterceptor
    {
        internal readonly TaskCompletionSource Entered = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        internal readonly TaskCompletionSource Release = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private int _armed;

        internal void Arm() => Volatile.Write(ref _armed, 1);

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default
        )
        {
            if (
                command.CommandText.Contains("FROM automation_flow_runs", StringComparison.Ordinal)
                && Interlocked.Exchange(ref _armed, 0) == 1
            )
            {
                _ = Entered.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }

    private sealed class UserCancellationAdmissionBarrier(bool holdAfterLock) : DbCommandInterceptor
    {
        internal readonly TaskCompletionSource<int> Entered = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        internal readonly TaskCompletionSource<int> CancellationAttempted = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        internal readonly TaskCompletionSource Release = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private int _armed;
        private int _touches;
        private DbCommand? _admission;

        internal void Arm() => Volatile.Write(ref _armed, 1);

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            if (
                Volatile.Read(ref _armed) == 1
                && command.CommandText.StartsWith(
                    "UPDATE automation_flow_runs",
                    StringComparison.Ordinal
                )
            )
            {
                if (
                    command.CommandText.Contains("'Cancelled'", StringComparison.Ordinal)
                    || command
                        .Parameters.Cast<DbParameter>()
                        .Any(parameter => Equals(parameter.Value, "Cancelled"))
                )
                {
                    _ = CancellationAttempted.TrySetResult(
                        ((NpgsqlConnection)command.Connection!).ProcessID
                    );
                }
                else if (
                    command.CommandText.Contains(
                        "SET \"ExecutionLeaseId\"",
                        StringComparison.Ordinal
                    )
                    && !command.CommandText.Contains("IS NULL", StringComparison.Ordinal)
                    && Interlocked.Increment(ref _touches) == 2
                )
                {
                    _admission = command;
                    if (!holdAfterLock)
                    {
                        _ = Entered.TrySetResult(((NpgsqlConnection)command.Connection!).ProcessID);
                        await Release.Task.WaitAsync(cancellationToken);
                    }
                }
            }
            return result;
        }

        public override async ValueTask<int> NonQueryExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            int result,
            CancellationToken cancellationToken = default
        )
        {
            if (holdAfterLock && ReferenceEquals(command, _admission))
            {
                _ = Entered.TrySetResult(((NpgsqlConnection)command.Connection!).ProcessID);
                await Release.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }
}
