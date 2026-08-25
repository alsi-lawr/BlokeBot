using System.Collections.Immutable;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Functional;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Automations;

public sealed class AutomationRunQueryService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    HostFeatureService features,
    AutomationCatalogService catalog
)
{
    public async Task<AutomationRunQueryOutcome> ListAsync(
        AutomationHostId hostId,
        CancellationToken cancellationToken
    )
    {
        var loaded = await features.Load(hostId.Value).RunAsync(cancellationToken);
        if (!loaded.Match(static _ => true, static () => false))
        {
            return new AutomationRunQueryOutcome.HostNotFound();
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var runs = await db
            .AutomationFlowRuns.AsNoTracking()
            .Include(static value => value.NodeRuns)
            .Where(value => value.HostId == hostId.Value)
            .OrderByDescending(static value => value.StartedAtUtc)
            .ToArrayAsync(cancellationToken);
        return new AutomationRunQueryOutcome.Available(runs.Select(Summary).ToImmutableArray());
    }

    private AutomationRunSummary Summary(AutomationFlowRun run)
    {
        var frozen =
            AutomationRuntimeSerialization.RestoreDefinition(run.DefinitionJson)
            as AutomationDefinitionRestoreOutcome.Available;
        return new(
            new(run.Id),
            new(run.FlowId),
            (AutomationFlowRunState)run.Status,
            new DateTimeOffset(run.StartedAtUtc, TimeSpan.Zero),
            run.CompletedAtUtc is { } completed
                ? new DateTimeOffset(completed, TimeSpan.Zero)
                : null,
            run.NodeRuns.OrderBy(static value => value.Sequence)
                .Select(node => new AutomationNodeRunSummary(
                    new(node.NodeId),
                    (AutomationNodeRunState)node.Status,
                    node.OutcomeCode,
                    node.CompletedAtUtc is { } completed
                        ? new DateTimeOffset(completed, TimeSpan.Zero)
                        : null,
                    Diagnostics(node.OutputJson, frozen?.Flow, node.NodeId)
                ))
                .ToImmutableArray()
        );
    }

    private ImmutableArray<AutomationValueDiagnostic> Diagnostics(
        string? outputJson,
        AutomationRuntimeSerialization.PersistedFlow? flow,
        Guid nodeId
    )
    {
        var node = flow?.Nodes.SingleOrDefault(candidate => candidate.Id == nodeId);
        return
            outputJson is not null
            && node is not null
            && catalog.ValidatePersistedDefinition(AutomationRuntimeSerialization.Definition(node))
                is AutomationConfigurationCheck.Valid valid
            && AutomationDataValueSerialization.RestoreOutputs(outputJson)
                is AutomationOutputRestoreOutcome.Available restored
            && AutomationPureHandlerRegistry.ValidCheckpointShape(
                valid.Definition,
                restored.Outputs
            )
            ? AutomationDataValueSerialization.Diagnostics(restored.Outputs)
            : [];
    }
}
