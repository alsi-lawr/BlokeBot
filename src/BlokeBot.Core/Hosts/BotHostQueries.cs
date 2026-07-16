using BlokeBot.Core.Identity;
using BlokeBot.Functional;
using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Hosts;

internal static class BotHostQueries
{
    public static IO<Option<int>, Never> FindHostId(BlokeBotDbContext db, string login)
    {
        var normalized = LoginName.Parse(login);
        return IO<Option<int>, Never>.Create(async ct =>
        {
            var hostId = await db
                .Hosts.AsNoTracking()
                .Where(x => x.Login == normalized.Value)
                .Select(x => (int?)x.Id)
                .SingleOrDefaultAsync(ct);
            return Result<Option<int>, Never>.Success(
                hostId.HasValue ? Option<int>.Some(hostId.Value) : Option<int>.None
            );
        });
    }
}
