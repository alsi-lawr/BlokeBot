using BlokeBot.Features.Guessing.Replies;
using BlokeBot.Features.Commands;
using BlokeBot.Hosts;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Features.Guessing.HostSetup;

public sealed class GuessingHostSeeder(IDbContextFactory<BlokeBotDbContext> dbFactory)
    : IBotHostSeeder
{
    public async Task SeedAsync(int hostId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await db.Hosts.AnyAsync(x => x.Id == hostId, ct))
            return;

        if (!await db.CommandAliases.AnyAsync(x => x.HostId == hostId, ct))
        {
            foreach (var command in AppCommandCatalog.ForFeature(AppCommandFeature.Guessing))
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

        if (!await db.Profiles.AnyAsync(x => x.HostId == hostId, ct))
        {
            db.Profiles.Add(
                new GuessRoundProfile
                {
                    HostId = hostId,
                    Name = "Default",
                    Slug = "default",
                    IsDefault = true,
                    ReplySettings = ReplySettingsMapper.ToEntity(GuessingDefaults.Replies()),
                }
            );
        }

        await db.SaveChangesAsync(ct);
    }
}
