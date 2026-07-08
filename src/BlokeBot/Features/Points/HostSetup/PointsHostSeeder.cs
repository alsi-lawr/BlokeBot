using BlokeBot.Features.Commands;
using BlokeBot.Features.Points.Replies;
using BlokeBot.Hosts;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Features.Points.HostSetup;

public sealed class PointsHostSeeder(IDbContextFactory<BlokeBotDbContext> dbFactory)
    : IBotHostSeeder
{
    public async Task SeedAsync(int hostId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await db.Hosts.AnyAsync(x => x.Id == hostId, ct))
            return;

        if (!await db.PointsSettings.AnyAsync(x => x.HostId == hostId, ct))
            db.PointsSettings.Add(new PointsSettings { HostId = hostId });

        foreach (var (kind, aliases) in PointsDefaults.Aliases)
        {
            if (
                await db.CommandAliases.AnyAsync(
                    x => x.HostId == hostId && x.Kind == AppCommandCatalog.FromPointsKind(kind),
                    ct
                )
            )
                continue;

            foreach (var alias in aliases)
                db.CommandAliases.Add(
                    new CommandAlias
                    {
                        HostId = hostId,
                        Kind = AppCommandCatalog.FromPointsKind(kind),
                        Alias = alias,
                    }
                );
        }

        await db.SaveChangesAsync(ct);
    }
}
