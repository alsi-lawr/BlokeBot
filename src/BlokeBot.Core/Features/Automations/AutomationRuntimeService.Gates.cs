using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Automations;

public sealed partial class AutomationRuntimeService
{
    private async Task<AutomationRuntimeGate> AdmissionGateAsync(
        AutomationHostId hostId,
        CancellationToken cancellationToken
    )
    {
        var host = await LoadHostStateAsync(hostId, cancellationToken);
        return host switch
        {
            null => new AutomationRuntimeGate.HostNotFound(),
            { Features: var features } when !features.Contains(HostFeatureFlags.Automations) =>
                new AutomationRuntimeGate.Disabled(),
            _ => new AutomationRuntimeGate.Enabled(host.Generation, host.Features),
        };
    }

    private async Task<AutomationExecutionGate> EnforceExecutionGateAsync(
        BlokeBotDbContext db,
        AutomationFlowRun run,
        Guid leaseId,
        CancellationToken cancellationToken
    )
    {
        var host = await LoadHostStateAsync(new(run.HostId), cancellationToken);
        if (host is null)
        {
            return AutomationExecutionGate.HostNotFound;
        }

        var outcomeCode =
            host.Generation != run.AutomationGeneration ? "automation-stale"
            : !host.Features.Contains(
                AutomationRequiredFeatures.BackingFeatures(run.RequiredFeatures)
            )
                ? "required-feature-disabled"
            : null;
        return outcomeCode is null ? AutomationExecutionGate.Open
            : await InvalidateOwnedAsync(db, run, leaseId, outcomeCode, cancellationToken)
                ? AutomationExecutionGate.Invalidated
            : AutomationExecutionGate.OwnershipLost;
    }

    private async Task<AutomationRuntimeHostState?> LoadHostStateAsync(
        AutomationHostId hostId,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db
            .Hosts.AsNoTracking()
            .Where(value => value.Id == hostId.Value)
            .Select(static value => new AutomationRuntimeHostState(
                value.AutomationGeneration,
                value.EnabledFeatures
            ))
            .SingleOrDefaultAsync(cancellationToken);
    }

    private static AutomationResumeOutcome ExecutionBlocked(AutomationExecutionGate gate) =>
        gate switch
        {
            AutomationExecutionGate.HostNotFound => new(AutomationResumeStatus.NotFound),
            AutomationExecutionGate.Invalidated => new(AutomationResumeStatus.Invalidated),
            _ => throw new InvalidOperationException("Execution is not blocked."),
        };

    private abstract record AutomationRuntimeGate
    {
        private AutomationRuntimeGate() { }

        internal sealed record Enabled(int Generation, HostFeatureFlags Features)
            : AutomationRuntimeGate;

        internal sealed record Disabled : AutomationRuntimeGate;

        internal sealed record HostNotFound : AutomationRuntimeGate;
    }

    private sealed record AutomationRuntimeHostState(int Generation, HostFeatureFlags Features);

    private enum AutomationExecutionGate
    {
        Open,
        HostNotFound,
        Invalidated,
        OwnershipLost,
    }
}
