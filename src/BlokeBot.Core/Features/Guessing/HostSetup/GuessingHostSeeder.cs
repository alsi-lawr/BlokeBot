using BlokeBot.Core.Features.Commands;
using BlokeBot.Core.Features.Guessing.Commands;
using BlokeBot.Core.Features.Guessing.Replies;
using BlokeBot.Core.Hosts;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Guessing.HostSetup;

public sealed class GuessingHostSeeder(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    CommandStrategyCatalog<GuessCommandKind, AppCommandRouteState> commands
) : IBotHostSeeder
{
    public async Task SeedAsync(int hostId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await db.Hosts.AnyAsync(x => x.Id == hostId, ct))
        {
            return;
        }

        var defaultProfile = await db.Profiles.SingleOrDefaultAsync(
            x => x.HostId == hostId && x.IsDefault,
            ct
        );
        if (defaultProfile is null)
        {
            defaultProfile = new GuessRoundProfile
            {
                HostId = hostId,
                Name = "Default",
                Slug = "default",
                IsDefault = true,
                ReplySettings = ReplySettingsMapper.ToEntity(GuessingDefaults.Replies()),
            };
            _ = db.Profiles.Add(defaultProfile);
            _ = await db.SaveChangesAsync(ct);
        }

        foreach (var command in commands.Descriptors)
        {
            var appKind = GuessingAppCommandKindMap.ToAppKind(command.Kind);
            if (
                await db.CommandAliases.AnyAsync(
                    x =>
                        x.HostId == hostId
                        && x.GuessRoundProfileId == defaultProfile.Id
                        && x.Kind == appKind,
                    ct
                )
            )
            {
                continue;
            }

            foreach (var alias in command.DefaultAliases)
            {
                _ = db.CommandAliases.Add(
                    new CommandAlias
                    {
                        HostId = hostId,
                        GuessRoundProfileId = defaultProfile.Id,
                        Kind = appKind,
                        Alias = alias,
                    }
                );
            }
        }

        _ = await db.SaveChangesAsync(ct);
    }
}
