using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.BlokeRaid;

internal sealed record BlokeRaidPublicSummary(
    string BossName,
    int CurrentHealth,
    int MaximumHealth,
    bool IsActive
);

public sealed partial class BlokeRaidService
{
    internal async Task<BlokeRaidPublicSummary?> GetPublicSummaryAsync(
        int hostId,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await FeatureIsEnabledAsync(db, hostId, ct))
        {
            return null;
        }
        var campaigns = await db
            .BlokeRaidCampaigns.AsNoTracking()
            .Where(value => value.HostId == hostId)
            .OrderByDescending(value => value.StartedAtUtc)
            .Take(2)
            .Select(value => new BlokeRaidPublicSummary(
                value.BossName,
                value.CurrentHealth,
                value.MaximumHealth,
                value.Status == BlokeRaidCampaignStatus.Active
            ))
            .ToArrayAsync(ct);
        return campaigns.FirstOrDefault(value => value.IsActive) ?? campaigns.FirstOrDefault();
    }
}
