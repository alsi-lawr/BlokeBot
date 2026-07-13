using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Features.Guessing.Rounds;

internal static class GuessingRoundQueries
{
    public static IQueryable<GuessRound> Open(BlokeBotDbContext db, int hostId)
    {
        return db
            .Rounds.Where(x => x.HostId == hostId)
            .Where(x => x.Status == GuessRoundStatus.Open)
            .OrderByDescending(x => x.StartedAtUtc);
    }

    public static IQueryable<GuessRound> Unresolved(BlokeBotDbContext db, int hostId)
    {
        return db
            .Rounds.Where(x => x.HostId == hostId)
            .Where(x => x.Status == GuessRoundStatus.Open || x.Status == GuessRoundStatus.Closed)
            .OrderByDescending(x => x.StartedAtUtc);
    }
}
