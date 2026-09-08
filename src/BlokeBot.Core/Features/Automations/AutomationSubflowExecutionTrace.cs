using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.Automations;

internal static class AutomationSubflowExecutionTrace
{
    internal static async Task CloseOpenAsync(
        BlokeBotDbContext db,
        AutomationFlowRun run,
        AutomationTraceOutcome outcome,
        DateTime now,
        CancellationToken cancellationToken,
        string? path = null
    )
    {
        if (
            AutomationRuntimeSerialization.RestoreDefinition(run.DefinitionJson)
                is not AutomationDefinitionRestoreOutcome.Available restored
            || restored.Flow.Invocations.IsDefault
        )
        {
            return;
        }
        foreach (
            var invocation in restored
                .Flow.Invocations.Reverse()
                .Where(invocation =>
                    path is null || invocation.Path.StartsWith(path, StringComparison.Ordinal)
                )
        )
        {
            var caller = run.NodeRuns.FirstOrDefault(node => node.NodeId == invocation.CallerId);
            if (
                caller is null
                || !run.NodeRuns.Any(node => node.NodeId == invocation.EntryId)
                || caller.OutcomeCode
                    is "subflow-completed"
                        or "subflow-failed"
                        or "subflow-terminated"
            )
            {
                continue;
            }
            caller.OutcomeCode = "subflow-terminated";
            caller.Status =
                outcome == AutomationTraceOutcome.Invalidated
                    ? AutomationNodeRunStatus.Invalidated
                    : AutomationNodeRunStatus.Failed;
            caller.CompletedAtUtc = now;
            caller.OutputJson = null;
            await AutomationTraceStore.AppendAsync(
                db,
                run.Id,
                AutomationTraceRedaction.Event(
                    run.Id,
                    AutomationTraceEventKind.SubflowExit,
                    now,
                    restored.Flow.Nodes.Single(node => node.Id == invocation.ExitId),
                    outcome
                ),
                now,
                cancellationToken
            );
        }
    }
}
