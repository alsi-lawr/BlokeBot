using BlokeBot.Features.Commands;
using BlokeBot.Identity;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Features.Points.Commands;

public sealed class PointsCommandService(IDbContextFactory<BlokeBotDbContext> dbFactory)
{
    public async Task<PointsCommandResolution> CreateResolutionAsync(
        int hostId,
        PointsCommandKind kind,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var settings =
            await db.PointsSettings.AsNoTracking().SingleOrDefaultAsync(x => x.HostId == hostId, ct)
            ?? new PointsSettings { HostId = hostId };
        return new PointsCommandResolution(hostId, kind, settings);
    }
}
