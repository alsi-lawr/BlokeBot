using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Automations;

public sealed partial class AutomationRuntimeService
{
    public Task<int> CancelRunAsync(
        AutomationHostId hostId,
        AutomationRunId runId,
        CancellationToken cancellationToken
    ) => CancelRunsAsync(hostId, runId, null, cancellationToken);

    public Task<int> CancelFlowRunsAsync(
        AutomationHostId hostId,
        AutomationFlowId flowId,
        CancellationToken cancellationToken
    ) => CancelRunsAsync(hostId, null, flowId, cancellationToken);

    public Task<int> CancelChannelRunsAsync(
        AutomationHostId hostId,
        CancellationToken cancellationToken
    ) => CancelRunsAsync(hostId, null, null, cancellationToken);

    private async Task<int> CancelRunsAsync(
        AutomationHostId hostId,
        AutomationRunId? runId,
        AutomationFlowId? flowId,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var active = db.AutomationFlowRuns.Where(run =>
            run.HostId == hostId.Value
            && (
                run.Status == AutomationFlowRunStatus.Running
                || run.Status == AutomationFlowRunStatus.Waiting
            )
        );
        if (runId is { } selectedRun)
        {
            active = active.Where(run => run.Id == selectedRun.Value);
        }
        if (flowId is { } selectedFlow)
        {
            active = active.Where(run => run.FlowId == selectedFlow.Value);
        }
        var ids = await active
            .OrderBy(run => run.Id)
            .Select(run => run.Id)
            .ToArrayAsync(cancellationToken);
        var changed = new List<Guid>();
        foreach (var id in ids)
        {
            var claimed = await active
                .Where(run => run.Id == id)
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(run => run.Status, AutomationFlowRunStatus.Cancelled)
                            .SetProperty(run => run.ExecutionLeaseId, (Guid?)null)
                            .SetProperty(run => run.CompletedAtUtc, clock.GetUtcNow().UtcDateTime),
                    cancellationToken
                );
            if (claimed == 0)
            {
                continue;
            }
            var run = await db
                .AutomationFlowRuns.Include(run => run.NodeRuns)
                .SingleAsync(run => run.Id == id, cancellationToken);
            var now = clock.GetUtcNow().UtcDateTime;
            foreach (
                var node in run.NodeRuns.Where(node =>
                    node.Status
                        is AutomationNodeRunStatus.Pending
                            or AutomationNodeRunStatus.Running
                            or AutomationNodeRunStatus.Waiting
                )
            )
            {
                node.Status = AutomationNodeRunStatus.Cancelled;
                node.OutcomeCode = "user-cancelled";
                node.CompletedAtUtc = now;
            }
            await TraceAsync(
                db,
                run,
                AutomationTraceEventKind.Cancellation,
                cancellationToken,
                outcome: AutomationTraceOutcome.Cancelled
            );
            await CompleteTerminalAsync(
                db,
                run,
                AutomationTraceOutcome.Cancelled,
                cancellationToken
            );
            _ = await db.SaveChangesAsync(cancellationToken);
            changed.Add(id);
        }
        await transaction.CommitAsync(cancellationToken);
        foreach (var id in changed)
        {
            await uiChanges.RunChangedAsync(hostId, new(id));
        }
        return changed.Count;
    }

    private async Task<bool> AdmitOwnedNodeAsync(
        AutomationNodeExecutionScope scope,
        AutomationInputResolution.Available inputs,
        CancellationToken cancellationToken
    )
    {
        var (db, run, _, node, _, leaseId) = scope;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (await TouchOwnedRunAsync(db, run.Id, leaseId, cancellationToken) == 0)
        {
            return false;
        }
        await TraceAsync(
            db,
            run,
            AutomationTraceEventKind.ResolvedInputs,
            cancellationToken,
            node,
            values: inputs.PortValues
        );
        await TraceAsync(db, run, AutomationTraceEventKind.Attempt, cancellationToken, node);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }
}
