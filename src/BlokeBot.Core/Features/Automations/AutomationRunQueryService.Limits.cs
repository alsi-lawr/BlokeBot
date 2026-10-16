using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Automations;

public sealed partial class AutomationRunQueryService
{
    public async Task<AutomationRunLimitsOutcome> ReadLimitsAsync(
        AutomationHostId host,
        AutomationFlowId? flow,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await ReadLimitsAsync(db, host, flow, cancellationToken);
    }

    public async Task<AutomationRunLimitsOutcome> SaveLimitsAsync(
        AutomationHostId host,
        AutomationFlowId? flow,
        AutomationRunLimits limits,
        CancellationToken cancellationToken
    )
    {
        if (limits.Unfinished is < 1 || limits.Waiting is < 1)
        {
            return new AutomationRunLimitsOutcome.Invalid();
        }
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (await MainDatabaseStatements.LockHostAsync(db, host.Value, cancellationToken) == 0)
        {
            return new AutomationRunLimitsOutcome.NotFound();
        }
        int changed;
        if (flow is { } id)
        {
            changed = await db
                .AutomationFlows.Where(value => value.HostId == host.Value && value.Id == id.Value)
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(value => value.MaximumUnfinishedRuns, limits.Unfinished)
                            .SetProperty(value => value.MaximumWaitingRuns, limits.Waiting),
                    cancellationToken
                );
        }
        else
        {
            changed = await db
                .Hosts.Where(value => value.Id == host.Value)
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(
                                value => value.MaximumUnfinishedAutomationRuns,
                                limits.Unfinished
                            )
                            .SetProperty(
                                value => value.MaximumWaitingAutomationRuns,
                                limits.Waiting
                            ),
                    cancellationToken
                );
        }
        if (changed == 0)
        {
            return new AutomationRunLimitsOutcome.NotFound();
        }
        var result = await ReadLimitsAsync(db, host, flow, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private static async Task<AutomationRunLimitsOutcome> ReadLimitsAsync(
        BlokeBotDbContext db,
        AutomationHostId host,
        AutomationFlowId? flow,
        CancellationToken cancellationToken
    )
    {
        var limits = flow is { } id
            ? await db
                .AutomationFlows.AsNoTracking()
                .Where(value => value.HostId == host.Value && value.Id == id.Value)
                .Select(value => new AutomationRunLimits(
                    value.MaximumUnfinishedRuns,
                    value.MaximumWaitingRuns
                ))
                .SingleOrDefaultAsync(cancellationToken)
            : await db
                .Hosts.AsNoTracking()
                .Where(value => value.Id == host.Value)
                .Select(value => new AutomationRunLimits(
                    value.MaximumUnfinishedAutomationRuns,
                    value.MaximumWaitingAutomationRuns
                ))
                .SingleOrDefaultAsync(cancellationToken);
        if (limits is null)
        {
            return new AutomationRunLimitsOutcome.NotFound();
        }
        var counts = await db
            .AutomationFlowRuns.AsNoTracking()
            .Where(value =>
                value.HostId == host.Value
                && (flow == null || value.FlowId == flow.Value.Value)
                && (
                    value.Status == AutomationFlowRunStatus.Running
                    || value.Status == AutomationFlowRunStatus.Waiting
                )
            )
            .GroupBy(value => value.HostId)
            .Select(group => new
            {
                Unfinished = group.Count(),
                Waiting = group.Count(value => value.Status == AutomationFlowRunStatus.Waiting),
            })
            .SingleOrDefaultAsync(cancellationToken);
        return new AutomationRunLimitsOutcome.Available(
            new(limits, counts?.Unfinished ?? 0, counts?.Waiting ?? 0)
        );
    }
}
