using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.Automations;

public sealed partial class AutomationRuntimeService
{
    private async Task<AutomationRuntimeSerialization.PersistedNode> UnwindFailureAsync(
        AutomationNodeExecutionScope scope,
        CancellationToken cancellationToken
    )
    {
        var (db, run, _, node, flow, _) = scope;
        var now = clock.GetUtcNow().UtcDateTime;
        var failureNode = node;
        foreach (var invocation in AutomationFrozenSubflows.Unwind(flow, node))
        {
            await AutomationSubflowExecutionTrace.CloseOpenAsync(
                db,
                run,
                AutomationTraceOutcome.Failed,
                now,
                cancellationToken,
                invocation.Path
            );
            foreach (
                var pending in run.NodeRuns.Where(value =>
                    value.Status
                        is AutomationNodeRunStatus.Pending
                            or AutomationNodeRunStatus.Running
                            or AutomationNodeRunStatus.Waiting
                )
            )
            {
                var pendingNode = flow.Nodes.Single(value => value.Id == pending.NodeId);
                if (
                    pendingNode.Invocation is { } nested
                    && flow.Invocations.Single(value => value.EntryId == nested.Id)
                        .Path.StartsWith(invocation.Path, StringComparison.Ordinal)
                )
                {
                    pending.Status = AutomationNodeRunStatus.Failed;
                    pending.OutcomeCode = "subflow-stopped";
                    pending.CompletedAtUtc = now;
                }
            }
            failureNode = flow.Nodes.Single(value => value.Id == invocation.CallerId);
            var caller = run.NodeRuns.Single(value => value.NodeId == invocation.CallerId);
            caller.Status = failureNode.ContinueOnFailure
                ? AutomationNodeRunStatus.ContinuedAfterFailure
                : AutomationNodeRunStatus.Failed;
            caller.OutcomeCode = "subflow-failed";
            caller.OutputJson = null;
            caller.CompletedAtUtc = now;
            await TraceAsync(
                db,
                run,
                AutomationTraceEventKind.Result,
                cancellationToken,
                failureNode,
                failureNode.ContinueOnFailure
                    ? AutomationTraceOutcome.ContinuedAfterFailure
                    : AutomationTraceOutcome.Failed
            );
        }
        return failureNode;
    }
}
