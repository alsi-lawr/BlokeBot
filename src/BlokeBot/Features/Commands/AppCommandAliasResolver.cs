using BlokeBot.Commands;
using BlokeBot.Identity;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Features.Commands;

public sealed class AppCommandAliasResolver(IDbContextFactory<BlokeBotDbContext> dbFactory)
{
    public async Task<AppCommandAliasResolution?> ResolveAsync(
        string hostLogin,
        string alias,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var normalizedHost = LoginName.Parse(hostLogin).Value;
        var hostId = await db
            .Hosts.AsNoTracking()
            .Where(x => x.Login == normalizedHost)
            .Select(x => (int?)x.Id)
            .SingleOrDefaultAsync(ct);
        if (hostId is null)
        {
            return null;
        }

        var normalizedAlias = CommandAliasNormalizer.Normalize(alias);
        var resolution = await db
            .CommandAliases.AsNoTracking()
            .Where(x => x.HostId == hostId.Value && x.Alias == normalizedAlias)
            .Select(x => new { x.Kind, x.GuessRoundProfileId })
            .FirstOrDefaultAsync(ct);

        return resolution is null
            ? null
            : new AppCommandAliasResolution(
                hostId.Value,
                resolution.Kind,
                CommandAliasScopePersistence.FromProfileId(resolution.GuessRoundProfileId)
            );
    }
}

public sealed record AppCommandAliasResolution(
    int HostId,
    AppCommandKind Kind,
    CommandAliasScope Scope
);
