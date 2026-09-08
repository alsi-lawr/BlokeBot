using System.Collections.Immutable;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using BlokeBot.Plugins.Features;

namespace BlokeBot.Core.Features.Automations;

public sealed partial class AutomationRuntimeService
{
    private async Task CompleteTerminalAsync(
        BlokeBotDbContext db,
        AutomationFlowRun run,
        AutomationTraceOutcome outcome,
        CancellationToken cancellationToken
    )
    {
        await AutomationSubflowExecutionTrace.CloseOpenAsync(
            db,
            run,
            outcome,
            clock.GetUtcNow().UtcDateTime,
            cancellationToken
        );
        _ = await AutomationSubflowRunReferences.RetireAsync(
            db,
            new(run.HostId),
            new(run.Id),
            cancellationToken
        );
        await TraceAsync(
            db,
            run,
            AutomationTraceEventKind.Terminal,
            cancellationToken,
            outcome: outcome
        );
    }

    private AutomationRuntimeSerialization.PersistedFlow? FreezeExecutionFences(
        AutomationRuntimeSerialization.PersistedFlow flow
    )
    {
        if (flow.Invocations.IsDefault)
        {
            return flow with
            {
                Nodes =
                [
                    .. flow.Nodes.Select(node =>
                        CurrentPluginProvenance(flow.HostId, node.DefinitionId) is { } current
                            ? node with
                            {
                                PluginProvenanceJson =
                                    PluginAutomationCatalogRegistry.SerializeProvenance(current),
                            }
                            : node
                    ),
                ],
            };
        }
        var nodes = ImmutableArray.CreateBuilder<AutomationRuntimeSerialization.PersistedNode>();
        foreach (var node in flow.Nodes)
        {
            if (
                catalog.ValidatePersistedDefinition(AutomationRuntimeSerialization.Definition(node))
                is not AutomationConfigurationCheck.Valid valid
            )
            {
                return null;
            }
            var current = CurrentPluginProvenance(flow.HostId, node.DefinitionId);
            var frozen = node with
            {
                PluginProvenanceJson = current is null
                    ? node.PluginProvenanceJson
                    : PluginAutomationCatalogRegistry.SerializeProvenance(current),
            };
            if (!flow.Invocations.IsDefault && frozen.Contract is null)
            {
                frozen = frozen with
                {
                    Contract = new(
                        new(node.Id),
                        valid.Definition.Kind,
                        valid.Definition.Display,
                        valid.Definition.Inputs,
                        valid.Definition.Outputs,
                        valid.Definition.Capabilities,
                        valid.Definition.RetrySafety
                    ),
                };
            }
            nodes.Add(frozen);
        }
        var result = flow with { Nodes = nodes.ToImmutable() };
        return
            AutomationFrozenSubflows.WithinSnapshotBound(result)
            && ExecutionDefinitionsAvailable(result)
            ? result
            : null;
    }

    private bool ExecutionDefinitionsAvailable(AutomationRuntimeSerialization.PersistedFlow flow)
    {
        foreach (var node in flow.Nodes)
        {
            if (
                catalog.ValidateAdmittedDefinition(
                    new(flow.HostId),
                    AutomationRuntimeSerialization.Definition(node)
                )
                    is not AutomationConfigurationCheck.Valid valid
                || (
                    valid.Definition.Kind
                        is AutomationNodeKind.Value
                            or AutomationNodeKind.Transform
                    && !catalog.Data.SupportsExecution(valid.Definition)
                )
            )
            {
                return false;
            }
            if (node.PluginProvenanceJson is not null)
            {
                if (
                    _pluginExecution is null
                    || !catalog.TryResolvePlugin(
                        new(flow.HostId),
                        new(node.DefinitionId),
                        out var plugin
                    )
                    || !plugin.Endpoint.State.Enabled
                )
                {
                    return false;
                }
                var independent =
                    plugin
                        .Endpoint.Declaration.FindFeature(plugin.Endpoint.State.Key.FeatureId)
                        ?.Twitch is
                    { Scopes.IsEmpty: true, EventSubTypes.IsEmpty: true };
                if (
                    !independent
                    && plugin.Endpoint.State.Readiness is not PluginFeatureReadiness.Ready
                )
                {
                    return false;
                }
            }
        }
        return true;
    }

    private async Task<bool> CompleteBoundaryAsync(
        AutomationNodeExecutionScope scope,
        AutomationInputResolution.Available inputs,
        CancellationToken cancellationToken
    )
    {
        var (db, run, nodeRun, node, flow, leaseId) = scope;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (await TouchOwnedRunAsync(db, run.Id, leaseId, cancellationToken) == 0)
        {
            return false;
        }
        var now = clock.GetUtcNow().UtcDateTime;
        var invocation =
            node.DefinitionId == AutomationSubflowDefinitions.Invoke
                ? flow.Invocations.Single(value => value.CallerId == node.Id)
                : flow.Invocations.Single(value => value.ExitId == node.Id);
        Guid nextSource;
        if (node.DefinitionId == AutomationSubflowDefinitions.Invoke)
        {
            var entry = flow.Nodes.Single(value => value.Id == invocation.EntryId);
            _ = db.AutomationNodeRuns.Add(
                new()
                {
                    RunId = run.Id,
                    NodeId = entry.Id,
                    Sequence = run.NodeRuns.Max(value => value.Sequence) + 1,
                    Status = AutomationNodeRunStatus.Succeeded,
                    AvailableAtUtc = now,
                    StartedAtUtc = now,
                    CompletedAtUtc = now,
                    OutcomeCode = "subflow-entered",
                    OutputJson = AutomationDataValueSerialization.SerializeOutputs(
                        inputs.PortValues
                    ),
                }
            );
            await TraceAsync(
                db,
                run,
                AutomationTraceEventKind.SubflowEntry,
                cancellationToken,
                entry,
                AutomationTraceOutcome.Succeeded,
                values: inputs.PortValues
            );
            nextSource = entry.Id;
        }
        else
        {
            var caller = run.NodeRuns.Single(value => value.NodeId == invocation.CallerId);
            caller.Status = AutomationNodeRunStatus.Succeeded;
            caller.OutputJson = AutomationDataValueSerialization.SerializeOutputs(
                inputs.PortValues
            );
            caller.CompletedAtUtc = now;
            caller.OutcomeCode = "subflow-completed";
            await TraceAsync(
                db,
                run,
                AutomationTraceEventKind.SubflowExit,
                cancellationToken,
                node,
                AutomationTraceOutcome.Succeeded,
                values: inputs.PortValues
            );
            nextSource = node.Id;
        }
        nodeRun.Status =
            node.DefinitionId == AutomationSubflowDefinitions.Invoke
                ? AutomationNodeRunStatus.Waiting
                : AutomationNodeRunStatus.Succeeded;
        nodeRun.CompletedAtUtc =
            node.DefinitionId == AutomationSubflowDefinitions.Invoke ? null : now;
        nodeRun.OutcomeCode =
            node.DefinitionId == AutomationSubflowDefinitions.Invoke
                ? "subflow-entered"
                : "subflow-exited";
        if (node.DefinitionId == AutomationSubflowDefinitions.Exit)
        {
            await TraceAsync(
                db,
                run,
                AutomationTraceEventKind.Result,
                cancellationToken,
                flow.Nodes.Single(value => value.Id == invocation.CallerId),
                AutomationTraceOutcome.Succeeded
            );
            await TraceAsync(
                db,
                run,
                AutomationTraceEventKind.Result,
                cancellationToken,
                node,
                AutomationTraceOutcome.Succeeded
            );
        }
        var next = AutomationFlowTraversal.Schedule(
            flow.Edges,
            nextSource,
            "complete",
            run.NodeRuns.Select(value => value.NodeId).ToHashSet()
        );
        AddPending(db, run.Id, next, now, run.NodeRuns.Max(value => value.Sequence) + 1);
        await TraceScheduledAsync(db, run, flow, next, now, cancellationToken);
        _ = await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }
}
