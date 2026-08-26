using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Features;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Automations;

public sealed class PluginAutomationRunCoordinator(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    TimeProvider clock
)
{
    internal Task CancelAsync(PluginFeatureState state, CancellationToken cancellationToken) =>
        InvalidateAsync(
            provenance =>
                provenance.PluginId == state.Key.PluginId.Value
                && provenance.FeatureId == state.Key.FeatureId.Value
                && provenance.LifecycleOperationId == state.Fence.OperationId.Value
                && provenance.WorkerGeneration == checked((long)state.Fence.Generation.Value)
                && provenance.FeatureGeneration == checked((long)state.Generation.Value),
            cancellationToken
        );

    internal Task CancelPluginAsync(PluginId pluginId, CancellationToken cancellationToken) =>
        InvalidateAsync(provenance => provenance.PluginId == pluginId.Value, cancellationToken);

    private async Task InvalidateAsync(
        Func<AutomationPluginProvenance, bool> matches,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var runs = await db
            .AutomationFlowRuns.Include(static run => run.NodeRuns)
            .Where(run =>
                run.Status == AutomationFlowRunStatus.Running
                || run.Status == AutomationFlowRunStatus.Waiting
            )
            .ToArrayAsync(cancellationToken);
        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var run in runs.Where(run => Contains(run.DefinitionJson, matches)))
        {
            run.Status = AutomationFlowRunStatus.Invalidated;
            run.CompletedAtUtc = now;
            run.ExecutionLeaseId = null;
            foreach (
                var node in run.NodeRuns.Where(static node =>
                    node.Status
                        is AutomationNodeRunStatus.Pending
                            or AutomationNodeRunStatus.Running
                )
            )
            {
                node.Status = AutomationNodeRunStatus.Invalidated;
                node.OutcomeCode = "plugin-generation-stopped";
                node.CompletedAtUtc = now;
            }
        }
        _ = await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static bool Contains(
        string definitionJson,
        Func<AutomationPluginProvenance, bool> matches
    ) =>
        AutomationRuntimeSerialization.RestoreDefinition(definitionJson)
            is AutomationDefinitionRestoreOutcome.Available available
        && available.Flow.Nodes.Any(node =>
            PluginAutomationCatalogRegistry.TryDeserializeProvenance(
                node.PluginProvenanceJson,
                out var provenance
            ) && matches(provenance)
        );
}
