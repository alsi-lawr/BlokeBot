using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.PlayWithViewers;

public sealed record PublicPlayQueueDestination(string Slug, string Name, string ActivityName);

public sealed partial class PlayQueueService
{
    // Navigation does not need participant rows, fields, history, or moderator projections.
    public async Task<IReadOnlyList<PublicPlayQueueDestination>> GetPublicDestinationsAsync(
        int hostId,
        int count,
        CancellationToken ct
    )
    {
        if (!await FeatureIsEnabledAsync(hostId, ct))
        {
            return [];
        }
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db
            .PlayQueues.AsNoTracking()
            .Where(value => value.HostId == hostId && value.IsOpen)
            .OrderBy(value => value.Name)
            .ThenBy(value => value.Slug)
            .Take(Math.Clamp(count, 0, 5))
            .Select(value => new PublicPlayQueueDestination(
                value.Slug,
                value.Name,
                value.ActivityName
            ))
            .ToArrayAsync(ct);
    }
}
