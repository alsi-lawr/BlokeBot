using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Automations;

internal sealed class AutomationFeatureDisableObserver(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    TimeProvider clock
) : IHostFeatureChangeObserver
{
    public async ValueTask FeatureChangedAsync(
        int hostId,
        HostFeatureFlags feature,
        bool enabled,
        CancellationToken cancellationToken
    )
    {
        if (enabled)
        {
            return;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var host = await db.Hosts.SingleOrDefaultAsync(
            value => value.Id == hostId,
            cancellationToken
        );
        if (host is null)
        {
            return;
        }

        var candidates = await db
            .AutomationFlowRuns.AsNoTracking()
            .Where(value =>
                value.HostId == hostId
                && (
                    value.Status == AutomationFlowRunStatus.Running
                    || value.Status == AutomationFlowRunStatus.Waiting
                )
            )
            .Select(static value => new { value.Id, value.RequiredFeatures })
            .ToArrayAsync(cancellationToken);
        var runs = candidates
            .Where(run =>
                feature == HostFeatureFlags.Automations || run.RequiredFeatures.Contains(feature)
            )
            .ToArray();
        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var run in runs)
        {
            var invalidated = await db
                .AutomationFlowRuns.Where(value =>
                    value.Id == run.Id
                    && (
                        value.Status == AutomationFlowRunStatus.Running
                        || value.Status == AutomationFlowRunStatus.Waiting
                    )
                )
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(
                                static value => value.Status,
                                AutomationFlowRunStatus.Invalidated
                            )
                            .SetProperty(static value => value.CompletedAtUtc, now),
                    cancellationToken
                );
            if (invalidated == 0)
            {
                continue;
            }

            _ = await db
                .AutomationNodeRuns.Where(value =>
                    value.RunId == run.Id
                    && (
                        value.Status == AutomationNodeRunStatus.Pending
                        || value.Status == AutomationNodeRunStatus.Running
                    )
                )
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(
                                static value => value.Status,
                                AutomationNodeRunStatus.Invalidated
                            )
                            .SetProperty(static value => value.OutcomeCode, "automation-disabled")
                            .SetProperty(static value => value.CompletedAtUtc, now),
                    cancellationToken
                );
        }

        _ = await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
