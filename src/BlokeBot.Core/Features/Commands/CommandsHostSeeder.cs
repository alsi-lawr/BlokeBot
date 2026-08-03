using BlokeBot.Core.Hosts;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Commands;

public sealed class CommandsHostSeeder(IDbContextFactory<BlokeBotDbContext> dbFactory)
    : IBotHostSeeder
{
    public async Task SeedAsync(int hostId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var host = await db.Hosts.SingleOrDefaultAsync(x => x.Id == hostId, ct);
        if (host is null || host.CommandsAliasesConfigured)
        {
            return;
        }

        const string DefaultAlias = "commands";
        var conflict =
            await db.CommandAliases.AnyAsync(x => x.HostId == hostId && x.Alias == DefaultAlias, ct)
            || await db.CustomCommandAliases.AnyAsync(
                x => x.HostId == hostId && x.Alias == DefaultAlias,
                ct
            );
        if (conflict)
        {
            host.CommandsDefaultConflictAlias = DefaultAlias;
        }
        else
        {
            _ = db.CommandAliases.Add(
                new CommandAlias
                {
                    HostId = hostId,
                    Kind = AppCommandKind.Commands,
                    Alias = DefaultAlias,
                }
            );
        }

        host.CommandsAliasesConfigured = true;
        _ = await db.SaveChangesAsync(ct);
    }
}
