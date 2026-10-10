using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using static BlokeBot.Core.Features.Automations.ExpandedAutomationContext;

namespace BlokeBot.Core.Features.Automations;

public abstract record AutomationManualRunOutcome
{
    private AutomationManualRunOutcome() { }

    public sealed record Dispatched(AutomationDispatchOutcome Dispatch)
        : AutomationManualRunOutcome;

    public sealed record Unavailable : AutomationManualRunOutcome;
}

public sealed class AutomationManualRunService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    AutomationRuntimeService runtime,
    AutomationCatalogService catalog,
    TimeProvider clock
)
{
    public async Task<AutomationManualRunOutcome> RunAsync(
        AutomationHostId authorizedHost,
        AutomationFlowId flowId,
        CancellationToken cancellation
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellation);
        var host = await db
            .Hosts.AsNoTracking()
            .SingleOrDefaultAsync(h => h.Id == authorizedHost.Value, cancellation);
        var flow = await db
            .AutomationFlows.AsNoTracking()
            .Include(f => f.Nodes)
            .SingleOrDefaultAsync(
                f => f.Id == flowId.Value && f.HostId == authorizedHost.Value && f.IsEnabled,
                cancellation
            );
        if (
            host is null
            || flow is null
            || !host.EnabledFeatures.Contains(HostFeatureFlags.Automations)
        )
        {
            return new AutomationManualRunOutcome.Unavailable();
        }
        var configured = new List<ManualSourceConfiguration>();
        foreach (
            var source in flow.Nodes.Where(n =>
                n.DefinitionId == AutomationDefinitionIds.ManualSource.Value
            )
        )
        {
            using var json = System.Text.Json.JsonDocument.Parse(source.ConfigurationJson);
            if (
                catalog.ValidatePersistedDefinition(
                    new(
                        source.DefinitionId,
                        source.DefinitionSchemaVersion,
                        json.RootElement.Clone()
                    )
                )
                is not AutomationConfigurationCheck.Valid
                {
                    Configuration: ManualSourceConfiguration manual
                }
            )
            {
                return new AutomationManualRunOutcome.Unavailable();
            }
            configured.Add(manual);
        }
        if (configured.Count == 0)
        {
            return new AutomationManualRunOutcome.Unavailable();
        }
        var invocation = Guid.NewGuid().ToString();
        var now = clock.GetUtcNow();
        var runs = System.Collections.Immutable.ImmutableArray.CreateBuilder<AutomationRunId>();
        var status = AutomationDispatchStatus.NoMatchingFlow;
        foreach (var manual in configured.Distinct())
        {
            var observed = await runtime.DispatchExpandedAsync(
                Create(
                    host,
                    AutomationDefinitionIds.ManualSource,
                    invocation,
                    now,
                    now,
                    [Text("manual-data", manual.Data)]
                ),
                c => c == manual,
                cancellation,
                selectedFlow: flowId
            );
            runs.AddRange(observed.RunIds);
            if (
                observed.Status == AutomationDispatchStatus.Accepted
                || status != AutomationDispatchStatus.Accepted
            )
            {
                status = observed.Status;
            }
        }
        var outcome = new AutomationDispatchOutcome(status, runs.ToImmutable());
        return new AutomationManualRunOutcome.Dispatched(outcome);
    }

    public async Task<AutomationManualRunOutcome> RunSourceAsync(
        AutomationHostId authorizedHost,
        AutomationFlowId flowId,
        AutomationNodeId sourceNodeId,
        ManualSourceConfiguration? dataOverride,
        CancellationToken cancellation
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellation);
        var host = await db
            .Hosts.AsNoTracking()
            .SingleOrDefaultAsync(h => h.Id == authorizedHost.Value, cancellation);
        var source = await db
            .AutomationFlowNodes.AsNoTracking()
            .SingleOrDefaultAsync(
                n =>
                    n.Id == sourceNodeId.Value
                    && n.FlowId == flowId.Value
                    && n.Flow.HostId == authorizedHost.Value
                    && n.Flow.IsEnabled
                    && n.DefinitionId == AutomationDefinitionIds.ManualSource.Value,
                cancellation
            );
        if (
            host is null
            || source is null
            || !host.EnabledFeatures.Contains(HostFeatureFlags.Automations)
        )
        {
            return new AutomationManualRunOutcome.Unavailable();
        }
        using var json = System.Text.Json.JsonDocument.Parse(source.ConfigurationJson);
        if (
            catalog.ValidatePersistedDefinition(
                new(source.DefinitionId, source.DefinitionSchemaVersion, json.RootElement.Clone())
            )
                is not AutomationConfigurationCheck.Valid
                {
                    Configuration: ManualSourceConfiguration saved
                }
            || (
                dataOverride is not null
                && await catalog.ValidateForSaveAsync(
                    authorizedHost,
                    AutomationDefinitionIds.ManualSource,
                    new(source.DefinitionSchemaVersion),
                    dataOverride,
                    cancellation
                )
                    is not AutomationConfigurationCheck.Valid
            )
        )
        {
            return new AutomationManualRunOutcome.Unavailable();
        }
        var now = clock.GetUtcNow();
        var dispatch = await runtime.DispatchExpandedAsync(
            Create(
                host,
                AutomationDefinitionIds.ManualSource,
                Guid.NewGuid().ToString(),
                now,
                now,
                [Text("manual-data", (dataOverride ?? saved).Data)]
            ),
            configuration => configuration == saved,
            cancellation,
            selectedFlow: flowId,
            selectedSource: sourceNodeId
        );
        return new AutomationManualRunOutcome.Dispatched(dispatch);
    }
}
