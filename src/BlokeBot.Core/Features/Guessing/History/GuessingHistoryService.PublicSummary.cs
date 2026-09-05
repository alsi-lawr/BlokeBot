using BlokeBot.Core.Features.ViewerPassports;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Guessing.History;

public sealed partial class GuessingHistoryService
{
    internal async Task<GuessLeaderboardEntry?> LoadPublicLeaderAsync(
        int hostId,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var excluded = ViewerPassportPublicIdentityPolicy.ExcludedLogins(db, hostId);
        return await db
            .Votes.AsNoTracking()
            .Where(value =>
                value.GuessRound != null
                && value.GuessRound.GuessRoundProfile != null
                && value.GuessRound.GuessRoundProfile.HostId == hostId
                && !excluded.Contains(value.Login)
            )
            .GroupBy(value => value.Login)
            .Select(group => new GuessLeaderboardEntry
            {
                Login = group.Key,
                CorrectGuesses = group.Count(value =>
                    value.GuessRound!.Status == GuessRoundStatus.Completed
                    && value.GuessRound.WinningName != null
                    && value.GuessName == value.GuessRound.WinningName
                ),
                RoundsPlayed = group.Count(),
                LastGuessAtUtc = group.Max(value => value.GuessedAtUtc),
            })
            .OrderByDescending(value => value.CorrectGuesses)
            .ThenByDescending(value => value.RoundsPlayed)
            .ThenBy(value => value.Login)
            .FirstOrDefaultAsync(ct);
    }
}
