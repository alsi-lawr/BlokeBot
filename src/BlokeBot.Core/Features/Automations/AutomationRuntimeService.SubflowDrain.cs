using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.Automations;

public sealed partial class AutomationRuntimeService
{
    private static HashSet<Guid> ActiveNodes(AutomationFlowRun run) =>
        run
            .NodeRuns.Where(node =>
                node.Status
                    is AutomationNodeRunStatus.Pending
                        or AutomationNodeRunStatus.Running
                        or AutomationNodeRunStatus.Waiting
            )
            .Select(node => node.NodeId)
            .ToHashSet();

    private static async Task WaitForSubtreeAsync(
        AutomationNodeExecutionScope scope,
        CancellationToken cancellationToken
    )
    {
        var (db, run, nodeRun, _, _, leaseId) = scope;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (await TouchOwnedRunAsync(db, run.Id, leaseId, cancellationToken) == 0)
        {
            return;
        }
        nodeRun.Status = AutomationNodeRunStatus.Waiting;
        nodeRun.OutcomeCode = "subflow-draining";
        _ = await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static AutomationNodeRun[] DrainedExits(
        AutomationFlowRun run,
        AutomationRuntimeSerialization.PersistedFlow flow
    )
    {
        var active = ActiveNodes(run);
        return flow.Invocations.IsDefault
            ? []
            : run
                .NodeRuns.Where(node =>
                    node.Status == AutomationNodeRunStatus.Waiting
                    && flow.Invocations.Any(invocation =>
                        invocation.ExitId == node.NodeId
                        && !AutomationFrozenSubflows.HasActiveDescendants(flow, invocation, active)
                    )
                )
                .ToArray();
    }

    private async Task<bool> QueueDrainedExitsAsync(
        BlokeBotDbContext db,
        AutomationFlowRun run,
        Guid leaseId,
        CancellationToken cancellationToken
    )
    {
        if (
            !run.NodeRuns.Any(node => node.Status == AutomationNodeRunStatus.Waiting)
            || AutomationRuntimeSerialization.RestoreDefinition(run.DefinitionJson)
                is not AutomationDefinitionRestoreOutcome.Available restored
            || restored.Flow.Invocations.IsDefault
        )
        {
            return false;
        }
        var ready = DrainedExits(run, restored.Flow);
        if (ready.Length == 0)
        {
            return false;
        }
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (await TouchOwnedRunAsync(db, run.Id, leaseId, cancellationToken) == 0)
        {
            return true;
        }
        foreach (var exit in ready)
        {
            exit.Status = AutomationNodeRunStatus.Pending;
            exit.AvailableAtUtc = clock.GetUtcNow().UtcDateTime;
        }
        _ = await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }
}
