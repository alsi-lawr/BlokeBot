using BlokeBot.Features.Commands;
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

        foreach (var command in AppCommandCatalog.ForFeature(AppCommandFeature.Points))
        {
            if (
                await db.CommandAliases.AnyAsync(
                    x => x.HostId == hostId && x.Kind == command.Kind,
                    ct
                )
            )
                continue;

            foreach (var alias in command.DefaultAliases)
                db.CommandAliases.Add(
                    new CommandAlias
                    {
                        HostId = hostId,
                        Kind = command.Kind,
                        Alias = alias,
                    }
                );
        }

        await db.SaveChangesAsync(ct);
    }
}
