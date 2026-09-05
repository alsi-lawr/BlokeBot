using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Moments;

internal sealed record MomentPublicSummaryItem(
    string Title,
    string Category,
    DateTime? ApprovedAtUtc
);

public sealed partial class MomentHubService
{
    internal async Task<IReadOnlyList<MomentPublicSummaryItem>?> GetWeeklySummaryAsync(
        int hostId,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        if (!await FeatureIsEnabledAsync(hostId, ct))
        {
            return null;
        }
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var start = MomentInput.WeekStart(nowUtc);
        var end = start.AddDays(7);
        return await db
            .MomentCandidates.AsNoTracking()
            .Where(value =>
                value.HostId == hostId
                && value.State == MomentCandidateState.Approved
                && value.ApprovedAtUtc >= start
                && value.ApprovedAtUtc < end
            )
            .OrderByDescending(value => value.ApprovedAtUtc)
            .ThenByDescending(value => value.Votes.Count)
            .ThenBy(value => value.PublicId)
            .Take(5)
            .Select(value => new MomentPublicSummaryItem(
                value.PublicTitle,
                value.PublicCategory,
                value.ApprovedAtUtc
            ))
            .ToArrayAsync(ct);
    }
}
