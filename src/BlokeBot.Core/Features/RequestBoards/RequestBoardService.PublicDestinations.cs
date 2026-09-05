using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.RequestBoards;

public sealed record PublicRequestBoardDestination(string Slug, string Title);

public sealed partial class RequestBoardService
{
    public async Task<IReadOnlyList<PublicRequestBoardDestination>> GetPublicDestinationsAsync(
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
            .RequestBoards.AsNoTracking()
            .Where(value => value.HostId == hostId && value.IsOpen)
            .OrderBy(value => value.Title)
            .ThenBy(value => value.Slug)
            .Take(Math.Clamp(count, 0, 5))
            .Select(value => new PublicRequestBoardDestination(value.Slug, value.Title))
            .ToArrayAsync(ct);
    }
}
