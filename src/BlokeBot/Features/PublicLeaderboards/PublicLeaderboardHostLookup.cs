using BlokeBot.Identity;
using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Features.PublicLeaderboards;

public sealed class PublicLeaderboardHostLookup(IDbContextFactory<BlokeBotDbContext> dbFactory)
{
    public async Task<PublicLeaderboardHost?> FindAsync(string channel, CancellationToken ct)
    {
        var login = LoginName.Parse(channel).Value;
        if (string.IsNullOrWhiteSpace(login))
        {
            return null;
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db
            .Hosts.AsNoTracking()
            .Where(x => x.Login == login)
            .Select(x => new PublicLeaderboardHost(x.Id, x.Login, x.DisplayName, x.EnabledFeatures))
            .SingleOrDefaultAsync(ct);
    }
}
