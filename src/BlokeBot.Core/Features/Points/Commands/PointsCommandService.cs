using BlokeBot.Core.Features.Replies;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Points.Commands;

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
        var delivery = await ReplyDeliverySettingWriter.LoadAsync(
            db,
            hostId,
            ReplyFeature.Points,
            ReplyDeliverySettingWriter.HostScopeId,
            ct
        );
        return new PointsCommandResolution(hostId, kind, settings, delivery);
    }
}
