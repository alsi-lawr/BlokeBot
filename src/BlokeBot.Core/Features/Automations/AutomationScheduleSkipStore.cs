using System.Collections.Immutable;
using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Automations;

public enum AutomationScheduleSkipReason
{
    Disabled,
    NotReady,
    Late,
    Offline,
    StreamStatusUnknown,
    FeatureDisabled,
    NoCurrentMatch,
    SourceReconstruction,
    SourceConfiguration,
    SubflowClosure,
    GraphValidation,
    ExecutionUnavailable,
    SubflowReferences,
    FlowUnfinishedLimit,
    FlowWaitingLimit,
    ChannelUnfinishedLimit,
    ChannelWaitingLimit,
}

public sealed record AutomationScheduleSkipFact(
    AutomationScheduleSkipReason Reason,
    DateTimeOffset ScheduledAtUtc,
    DateTimeOffset ObservedAtUtc
);

public sealed class AutomationScheduleSkipStore(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    TimeProvider clock
)
{
    public const int QueryLimit = 20;
    public const int CleanupBatchSize = 100;
    public static TimeSpan Retention { get; } = TimeSpan.FromHours(24);

    internal static async Task AppendAsync(
        BlokeBotDbContext db,
        AutomationHostId host,
        AutomationFlowId flow,
        AutomationNodeId source,
        AutomationScheduleSkipReason reason,
        DateTimeOffset scheduled,
        DateTimeOffset observed,
        CancellationToken ct
    )
    {
        _ = db.AutomationScheduleSkips.Add(
            new()
            {
                Id = Guid.NewGuid(),
                HostId = host.Value,
                FlowId = flow.Value,
                SourceNodeId = source.Value,
                Reason = (int)reason,
                ScheduledAtUtc = scheduled.UtcDateTime,
                ObservedAtUtc = observed.UtcDateTime,
                ExpiresAtUtc = observed.UtcDateTime + Retention,
            }
        );
        _ = await db.SaveChangesAsync(ct);
    }

    public async Task<ImmutableArray<AutomationScheduleSkipFact>> ListAsync(
        AutomationHostId host,
        AutomationFlowId flow,
        AutomationNodeId source,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var rows = await db
            .AutomationScheduleSkips.AsNoTracking()
            .Where(row =>
                row.HostId == host.Value
                && row.FlowId == flow.Value
                && row.SourceNodeId == source.Value
                && row.ExpiresAtUtc > now
            )
            .OrderByDescending(row => row.ObservedAtUtc)
            .ThenByDescending(row => row.Id)
            .Take(QueryLimit)
            .ToArrayAsync(ct);
        return
        [
            .. rows.Select(row => new AutomationScheduleSkipFact(
                (AutomationScheduleSkipReason)row.Reason,
                new(row.ScheduledAtUtc, TimeSpan.Zero),
                new(row.ObservedAtUtc, TimeSpan.Zero)
            )),
        ];
    }

    public async Task<int> DeleteExpiredAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var ids = db
            .AutomationScheduleSkips.Where(row => row.ExpiresAtUtc <= now)
            .OrderBy(row => row.ExpiresAtUtc)
            .ThenBy(row => row.Id)
            .Take(CleanupBatchSize)
            .Select(row => row.Id);
        return await db
            .AutomationScheduleSkips.Where(row => ids.Contains(row.Id))
            .ExecuteDeleteAsync(ct);
    }
}
