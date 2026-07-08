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
            return null;

        var normalizedAlias = CommandAliasNormalizer.Normalize(alias);
        var kind = await db
            .CommandAliases.AsNoTracking()
            .Where(x => x.HostId == hostId.Value && x.Alias == normalizedAlias)
            .Select(x => (AppCommandKind?)x.Kind)
            .FirstOrDefaultAsync(ct);

        return kind is null ? null : new AppCommandAliasResolution(hostId.Value, kind.Value);
    }
}

public sealed record AppCommandAliasResolution(int HostId, AppCommandKind Kind);
