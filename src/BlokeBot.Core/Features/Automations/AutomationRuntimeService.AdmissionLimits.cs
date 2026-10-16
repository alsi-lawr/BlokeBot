using System.Collections.Immutable;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Automations;

public sealed partial class AutomationRuntimeService
{
    internal static bool IsRunLimit(AutomationAdmissionRejectionReason reason) =>
        reason
            is AutomationAdmissionRejectionReason.FlowUnfinishedLimit
                or AutomationAdmissionRejectionReason.FlowWaitingLimit
                or AutomationAdmissionRejectionReason.ChannelUnfinishedLimit
                or AutomationAdmissionRejectionReason.ChannelWaitingLimit;

    private static async Task<AutomationAdmissionRejectionReason?> CheckRunLimitsAsync(
        BlokeBotDbContext db,
        AutomationFlow flow,
        AutomationRunLimits channel,
        CancellationToken cancellationToken
    )
    {
        if (
            flow.MaximumUnfinishedRuns is null
            && flow.MaximumWaitingRuns is null
            && channel.Unfinished is null
            && channel.Waiting is null
        )
        {
            return null;
        }
        var usage = await db
            .AutomationFlowRuns.AsNoTracking()
            .Where(run =>
                run.HostId == flow.HostId
                && (
                    run.Status == AutomationFlowRunStatus.Running
                    || run.Status == AutomationFlowRunStatus.Waiting
                )
            )
            .GroupBy(run => run.HostId)
            .Select(group => new
            {
                Unfinished = group.Count(),
                Waiting = group.Count(run => run.Status == AutomationFlowRunStatus.Waiting),
                FlowUnfinished = group.Count(run => run.FlowId == flow.Id),
                FlowWaiting = group.Count(run =>
                    run.FlowId == flow.Id && run.Status == AutomationFlowRunStatus.Waiting
                ),
            })
            .SingleOrDefaultAsync(cancellationToken);
        return flow.MaximumUnfinishedRuns is { } unfinished
            && (usage?.FlowUnfinished ?? 0) >= unfinished
                ? AutomationAdmissionRejectionReason.FlowUnfinishedLimit
            : flow.MaximumWaitingRuns is { } waiting && (usage?.FlowWaiting ?? 0) >= waiting
                ? AutomationAdmissionRejectionReason.FlowWaitingLimit
            : channel.Unfinished is { } channelUnfinished
            && (usage?.Unfinished ?? 0) >= channelUnfinished
                ? AutomationAdmissionRejectionReason.ChannelUnfinishedLimit
            : channel.Waiting is { } channelWaiting && (usage?.Waiting ?? 0) >= channelWaiting
                ? AutomationAdmissionRejectionReason.ChannelWaitingLimit
            : null;
    }

    private async Task PublishRunLimitRejectionsAsync(
        AutomationHostId host,
        ImmutableArray<AutomationAdmissionRejection> rejected
    )
    {
        foreach (
            var rejection in rejected
                .Where(value => IsRunLimit(value.Reason))
                .DistinctBy(value => (value.FlowId, value.Reason))
        )
        {
            await uiChanges.RunLimitRejectedAsync(host, rejection.FlowId, rejection.Reason);
        }
    }

    private static void UpdateFrontierStatus(AutomationFlowRun run, DateTime now)
    {
        if (Terminal(run.Status) is not null)
        {
            return;
        }
        run.Status =
            run.NodeRuns.Any(node =>
                node.Status == AutomationNodeRunStatus.Running
                || (node.Status == AutomationNodeRunStatus.Pending && node.AvailableAtUtc <= now)
            )
                ? AutomationFlowRunStatus.Running
            : run.NodeRuns.Any(node =>
                node.Status is AutomationNodeRunStatus.Pending or AutomationNodeRunStatus.Waiting
            )
                ? AutomationFlowRunStatus.Waiting
            : AutomationFlowRunStatus.Running;
    }

    private async Task<bool> SetOwnedWaitingAsync(
        BlokeBotDbContext db,
        AutomationFlowRun run,
        Guid leaseId,
        CancellationToken cancellationToken
    )
    {
        var changed = await OwnedActiveRuns(db, run.Id, leaseId)
            .ExecuteUpdateAsync(
                setters =>
                    setters.SetProperty(value => value.Status, AutomationFlowRunStatus.Waiting),
                cancellationToken
            );
        if (changed != 0 && run.Status != AutomationFlowRunStatus.Waiting)
        {
            await uiChanges.RunChangedAsync(new(run.HostId), new(run.Id));
        }
        return changed != 0;
    }
}
