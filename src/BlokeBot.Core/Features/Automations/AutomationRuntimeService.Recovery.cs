using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Automations;

public sealed partial class AutomationRuntimeService
{
    private async Task RecoverInterruptedAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var ids = await db
            .AutomationFlowRuns.Where(value =>
                value.ExecutionLeaseId != null
                || value.NodeRuns.Any(node =>
                    node.Status == AutomationNodeRunStatus.Running
                    || node.Status == AutomationNodeRunStatus.Waiting
                )
            )
            .OrderBy(value => value.Id)
            .Select(value => value.Id)
            .ToArrayAsync(cancellationToken);
        var runs = new List<AutomationFlowRun>();
        foreach (var id in ids)
        {
            var locked = await db
                .AutomationFlowRuns.Where(run =>
                    run.Id == id && run.Status != AutomationFlowRunStatus.Cancelled
                )
                .ExecuteUpdateAsync(
                    setters =>
                        setters.SetProperty(
                            run => run.ExecutionLeaseId,
                            run => run.ExecutionLeaseId
                        ),
                    cancellationToken
                );
            if (locked != 0)
            {
                runs.Add(
                    await db
                        .AutomationFlowRuns.Include(run => run.NodeRuns)
                        .SingleAsync(run => run.Id == id, cancellationToken)
                );
            }
        }
        if (runs.Count == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        var changed = await RecoverRunsAsync(db, [.. runs], cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        foreach (var recovered in changed)
        {
            await uiChanges.RunChangedAsync(new(recovered.HostId), new(recovered.Id));
        }
    }

    private async Task RecoverCancelledRunAsync(AutomationRunId runId, Guid leaseId)
    {
        await using var db = await dbFactory.CreateDbContextAsync(CancellationToken.None);
        await using var transaction = await db.Database.BeginTransactionAsync(
            CancellationToken.None
        );
        var owned = await db
            .AutomationFlowRuns.Where(run =>
                run.Id == runId.Value && run.ExecutionLeaseId == leaseId
            )
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(run => run.ExecutionLeaseId, leaseId),
                CancellationToken.None
            );
        if (owned == 0)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return;
        }
        var run = await db
            .AutomationFlowRuns.Include(run => run.NodeRuns)
            .SingleAsync(run => run.Id == runId.Value, CancellationToken.None);
        await TraceAsync(
            db,
            run,
            AutomationTraceEventKind.Cancellation,
            CancellationToken.None,
            outcome: AutomationTraceOutcome.Cancelled
        );
        var changed = await RecoverRunsAsync(db, [run], CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
        foreach (var recovered in changed)
        {
            await uiChanges.RunChangedAsync(new(recovered.HostId), new(recovered.Id));
        }
    }

    private async Task<AutomationFlowRun[]> RecoverRunsAsync(
        BlokeBotDbContext db,
        AutomationFlowRun[] runs,
        CancellationToken cancellationToken
    )
    {
        var previousStates = runs.ToDictionary(
            run => run.Id,
            run => (run.Status, run.CompletedAtUtc)
        );
        var hostIds = runs.Select(static value => value.HostId).Distinct().ToArray();
        var hosts = await db
            .Hosts.AsNoTracking()
            .Where(value => hostIds.Contains(value.Id))
            .ToDictionaryAsync(
                static value => value.Id,
                static value => new { value.EnabledFeatures, value.AutomationGeneration },
                cancellationToken
            );
        var interruptedNodeIds = runs.ToDictionary(
            run => run.Id,
            run =>
                run.NodeRuns.Where(node => node.Status == AutomationNodeRunStatus.Running)
                    .Select(node => node.NodeId)
                    .ToHashSet()
        );
        var existingNodeIds = runs.ToDictionary(
            run => run.Id,
            run => run.NodeRuns.Select(node => node.NodeId).ToHashSet()
        );
        var alreadyTerminal = runs.Where(run => Terminal(run.Status) is not null)
            .Select(run => run.Id)
            .ToHashSet();
        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var run in runs)
        {
            run.ExecutionLeaseId = null;
            var interrupted = run
                .NodeRuns.Where(static node => node.Status == AutomationNodeRunStatus.Running)
                .ToArray();
            if (interrupted.Length == 0)
            {
                continue;
            }

            if (Terminal(run.Status) is not null)
            {
                FinishInterruptedNodes(interrupted, run.Status, now);
                continue;
            }

            if (!hosts.TryGetValue(run.HostId, out var host))
            {
                Invalidate(run, now, "host-unavailable");
                continue;
            }

            if (host.AutomationGeneration != run.AutomationGeneration)
            {
                Invalidate(run, now, "automation-stale");
                continue;
            }

            if (
                !host.EnabledFeatures.Contains(
                    AutomationRequiredFeatures.BackingFeatures(run.RequiredFeatures)
                )
            )
            {
                Invalidate(run, now, "required-feature-disabled");
                continue;
            }

            if (
                AutomationRuntimeSerialization.RestoreDefinition(run.DefinitionJson)
                is not AutomationDefinitionRestoreOutcome.Available restoredDefinition
            )
            {
                FailMalformedRun(run, now);
                continue;
            }

            var flow = restoredDefinition.Flow;
            var definitions = flow.Nodes.ToDictionary(static node => node.Id);
            if (!flow.Invocations.IsDefault)
            {
                if (
                    AutomationRuntimeSerialization.RestoreContext(
                        run.ContextSchemaVersion,
                        run.ContextJson
                    )
                        is not AutomationContextRestoreOutcome.Available
                    || !ExecutionDefinitionsAvailable(flow)
                )
                {
                    Invalidate(run, now, "definition-execution-stale");
                    continue;
                }
                foreach (var interruptedNode in interrupted)
                {
                    var definition = definitions[interruptedNode.NodeId];
                    interruptedNode.Status = definition.ContinueOnFailure
                        ? AutomationNodeRunStatus.ContinuedAfterFailure
                        : AutomationNodeRunStatus.Failed;
                    interruptedNode.OutcomeCode = "execution-interrupted";
                    interruptedNode.CompletedAtUtc = now;
                    var failureNode = await UnwindFailureAsync(
                        new(db, run, interruptedNode, definition, flow, Guid.Empty),
                        cancellationToken
                    );
                    if (!failureNode.ContinueOnFailure)
                    {
                        FailInterruptedRun(run, interrupted, definitions, now);
                        break;
                    }
                    var next = AutomationFlowTraversal.Schedule(
                        flow.Edges,
                        AutomationFrozenSubflows.Continuation(flow, failureNode).Id,
                        new AutomationOutputSelection.Selected([new("complete")]),
                        run.NodeRuns.Select(node => node.NodeId).ToHashSet()
                    );
                    AddPending(db, run.Id, next, now, run.NodeRuns.Max(node => node.Sequence) + 1);
                }
                continue;
            }
            if (interrupted.Any(node => !definitions[node.NodeId].ContinueOnFailure))
            {
                FailInterruptedRun(run, interrupted, definitions, now);
                continue;
            }

            ContinueInterruptedRun(db, run, interrupted, definitions, flow, now);
        }

        foreach (var run in runs)
        {
            await TraceAsync(
                db,
                run,
                AutomationTraceEventKind.Recovery,
                cancellationToken,
                outcome: AutomationTraceOutcome.Interrupted
            );
            if (
                AutomationRuntimeSerialization.RestoreDefinition(run.DefinitionJson)
                is AutomationDefinitionRestoreOutcome.Available restored
            )
            {
                foreach (
                    var node in run.NodeRuns.Where(node =>
                        interruptedNodeIds[run.Id].Contains(node.NodeId)
                    )
                )
                {
                    await TraceAsync(
                        db,
                        run,
                        AutomationTraceEventKind.Result,
                        cancellationToken,
                        restored.Flow.Nodes.SingleOrDefault(value => value.Id == node.NodeId),
                        node.Status == AutomationNodeRunStatus.ContinuedAfterFailure
                                ? AutomationTraceOutcome.ContinuedAfterFailure
                            : node.Status == AutomationNodeRunStatus.Invalidated
                                ? AutomationTraceOutcome.Invalidated
                            : AutomationTraceOutcome.Interrupted
                    );
                }
                if (Terminal(run.Status) is null)
                {
                    foreach (var exit in DrainedExits(run, restored.Flow))
                    {
                        exit.Status = AutomationNodeRunStatus.Pending;
                        exit.AvailableAtUtc = now;
                    }
                }
                await TraceScheduledAsync(
                    db,
                    run,
                    restored.Flow,
                    run.NodeRuns.Where(node =>
                            !existingNodeIds[run.Id].Contains(node.NodeId)
                            && node.Status == AutomationNodeRunStatus.Pending
                        )
                        .Select(node => node.NodeId),
                    now,
                    cancellationToken
                );
            }
            if (!alreadyTerminal.Contains(run.Id) && Terminal(run.Status) is not null)
            {
                if (run.Status == AutomationFlowRunStatus.Invalidated)
                {
                    await TraceAsync(
                        db,
                        run,
                        AutomationTraceEventKind.Cancellation,
                        cancellationToken,
                        outcome: AutomationTraceOutcome.Invalidated
                    );
                }
                await CompleteTerminalAsync(
                    db,
                    run,
                    run.Status == AutomationFlowRunStatus.Completed
                            ? AutomationTraceOutcome.Succeeded
                        : run.Status == AutomationFlowRunStatus.Invalidated
                            ? AutomationTraceOutcome.Invalidated
                        : AutomationTraceOutcome.Failed,
                    cancellationToken
                );
            }
        }
        foreach (var run in runs)
        {
            UpdateFrontierStatus(run, now);
        }
        _ = await db.SaveChangesAsync(cancellationToken);
        return runs.Where(run => previousStates[run.Id] != (run.Status, run.CompletedAtUtc))
            .ToArray();
    }
}
