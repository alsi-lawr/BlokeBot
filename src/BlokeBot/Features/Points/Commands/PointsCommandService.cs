using BlokeBot.Features.Commands;
using BlokeBot.Identity;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Features.Points.Commands;

public sealed class PointsCommandService(IDbContextFactory<BlokeBotDbContext> dbFactory)
{
    public async Task<PointsCommandResolution?> ResolveCommandAsync(
        string hostLogin,
        string alias,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var normalizedHost = LoginName.Parse(hostLogin).Value;
        var host = await db
            .Hosts.AsNoTracking()
            .Where(x => x.Login == normalizedHost)
            .Select(x => new { x.Id })
            .SingleOrDefaultAsync(ct);
        if (host is null)
            return null;

        var normalizedAlias = CommandAliasNormalizer.Normalize(alias);
        var storedKind = await db
            .CommandAliases.AsNoTracking()
            .Where(x => x.HostId == host.Id && x.Alias == normalizedAlias)
            .Select(x => x.Kind)
            .FirstOrDefaultAsync(ct);
        if (!Enum.TryParse<PointsCommandKind>(storedKind, ignoreCase: true, out var kind))
            return null;

        var settings =
            await db
                .PointsSettings.AsNoTracking()
                .SingleOrDefaultAsync(x => x.HostId == host.Id, ct)
            ?? new PointsSettings { HostId = host.Id };
        return new PointsCommandResolution(host.Id, kind, settings);
    }
}
