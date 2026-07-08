using BlokeBot.Identity;
using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Hosts;

internal static class BotHostQueries
{
    public static async Task<int?> FindHostIdAsync(
        BlokeBotDbContext db,
        string login,
        CancellationToken ct
    )
    {
        var normalized = LoginName.Parse(login);
        return await db
            .Hosts.AsNoTracking()
            .Where(x => x.Login == normalized.Value)
            .Select(x => (int?)x.Id)
            .SingleOrDefaultAsync(ct);
    }
}
