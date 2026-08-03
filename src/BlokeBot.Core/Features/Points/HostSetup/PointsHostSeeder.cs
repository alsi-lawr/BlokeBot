using BlokeBot.Core.Features.Commands;
using BlokeBot.Core.Features.Points.Commands;
using BlokeBot.Core.Hosts;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Points.HostSetup;

public sealed class PointsHostSeeder(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    CommandStrategyCatalog<PointsCommandKind, AppCommandRouteState> commands
) : IBotHostSeeder
{
    public async Task SeedAsync(int hostId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await db.Hosts.AnyAsync(x => x.Id == hostId, ct))
        {
            return;
        }

        if (!await db.PointsSettings.AnyAsync(x => x.HostId == hostId, ct))
        {
            _ = db.PointsSettings.Add(new PointsSettings { HostId = hostId });
        }

        foreach (var command in commands.Descriptors)
        {
            var appKind = PointsAppCommandKindMap.ToAppKind(command.Kind);
            if (await db.CommandAliases.AnyAsync(x => x.HostId == hostId && x.Kind == appKind, ct))
            {
                continue;
            }

            foreach (var alias in command.DefaultAliases)
            {
                _ = db.CommandAliases.Add(
                    new CommandAlias
                    {
                        HostId = hostId,
                        Kind = appKind,
                        Alias = alias,
                    }
                );
            }
        }

        _ = await db.SaveChangesAsync(ct);
    }
}
