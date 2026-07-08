using BlokeBot.Features.Guessing.Rounds;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Features.Guessing.History;

public sealed class GuessingHistoryService(IDbContextFactory<BlokeBotDbContext> dbFactory)
{
    public async Task<GuessLeaderboardPage> LoadLeaderboardAsync(
        int hostId,
        GuessHistoryQuery query,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 10, 100);
        var votes = db
            .Votes.AsNoTracking()
            .Where(x =>
                x.GuessRound != null
                && x.GuessRound.GuessRoundProfile != null
                && x.GuessRound.GuessRoundProfile.HostId == hostId
            );

        if (query.FromUtc is { } fromUtc)
            votes = votes.Where(x => x.GuessedAtUtc >= fromUtc);

        if (query.ToUtc is { } toUtc)
            votes = votes.Where(x => x.GuessedAtUtc < toUtc);

        if (query.ProfileId is { } profileId and > 0)
            votes = votes.Where(x => x.GuessRound!.GuessRoundProfileId == profileId);

        if (!string.IsNullOrWhiteSpace(query.Username))
        {
            var username = query.Username.Trim().ToLowerInvariant();
            votes = votes.Where(x => x.Login.ToLower().Contains(username));
        }

        var shapedVotes = votes.Select(x => new
        {
            x.GuessedAtUtc,
            IsCorrect = x.GuessRound!.Status == Store(GuessRoundStatus.Completed)
                && x.GuessRound.WinningName != null
                && x.GuessName == x.GuessRound.WinningName,
            x.Login,
        });

        var totalGuesses = await shapedVotes.CountAsync(ct);
        var correctGuesses = await shapedVotes.CountAsync(x => x.IsCorrect, ct);
        var leaderboard = shapedVotes
            .GroupBy(x => x.Login)
            .Select(x => new GuessLeaderboardEntry
            {
                CorrectGuesses = x.Count(vote => vote.IsCorrect),
                LastGuessAtUtc = x.Max(vote => vote.GuessedAtUtc),
                Login = x.Key,
                RoundsPlayed = x.Count(),
            });

        var totalEntries = await leaderboard.CountAsync(ct);
        var entries = await leaderboard
            .OrderByDescending(x => x.CorrectGuesses)
            .ThenByDescending(x => x.RoundsPlayed)
            .ThenBy(x => x.Login)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        for (var i = 0; i < entries.Count; i++)
            entries[i].Rank = ((page - 1) * pageSize) + i + 1;

        return new GuessLeaderboardPage
        {
            CorrectGuesses = correctGuesses,
            Entries = entries,
            Page = page,
            PageSize = pageSize,
            TotalEntries = totalEntries,
            TotalGuesses = totalGuesses,
            TotalPlayers = totalEntries,
        };
    }

    private static string Store(GuessRoundStatus status) => status.ToString();
}
