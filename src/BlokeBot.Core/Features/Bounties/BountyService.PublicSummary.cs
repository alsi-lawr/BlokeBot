using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Bounties;

internal sealed record BountySummaryItem(
    string Title,
    BountyStatus Status,
    DateTime? ResolvedAtUtc
);

internal sealed record BountyPublicSummary(
    BountySummaryItem? First,
    bool IsActive,
    IReadOnlyList<BountySummaryItem> Completed
);

internal sealed partial class BountyService
{
    internal async Task<BountyPublicSummary?> GetPublicSummaryAsync(
        int hostId,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await FeatureIsEnabledAsync(db, hostId, ct))
        {
            return null;
        }
        var visible = db
            .Bounties.AsNoTracking()
            .Where(value => value.HostId == hostId && value.Visibility == BountyVisibility.Public);
        var first = await visible
            .OrderBy(value =>
                value.Status == BountyStatus.Completed
                || value.Status == BountyStatus.Failed
                || value.Status == BountyStatus.Expired
                || value.Status == BountyStatus.Cancelled
            )
            .ThenBy(value => value.ExpiresAtUtc)
            .ThenByDescending(value => value.CreatedAtUtc)
            .ThenBy(value => value.Id)
            .Select(value => new BountySummaryItem(value.Title, value.Status, value.ResolvedAtUtc))
            .FirstOrDefaultAsync(ct);
        var active = await visible.AnyAsync(
            value =>
                value.Status == BountyStatus.Proposed
                || value.Status == BountyStatus.Funding
                || value.Status == BountyStatus.Accepted,
            ct
        );
        var completed = await visible
            .Where(value => value.Status == BountyStatus.Completed && value.ResolvedAtUtc != null)
            .OrderByDescending(value => value.ResolvedAtUtc)
            .ThenBy(value => value.Id)
            .Take(5)
            .Select(value => new BountySummaryItem(value.Title, value.Status, value.ResolvedAtUtc))
            .ToArrayAsync(ct);
        return new(first, active, completed);
    }
}
