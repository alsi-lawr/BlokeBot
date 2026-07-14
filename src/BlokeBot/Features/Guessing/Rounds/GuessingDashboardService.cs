using System.Collections.Immutable;
using BlokeBot.Features.Guessing.Guesses;
using BlokeBot.Features.Guessing.Profiles;
using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Features.Guessing.Rounds;

public sealed class GuessingDashboardService(IDbContextFactory<BlokeBotDbContext> dbFactory)
{
    public async Task<GuessingDashboardState> LoadStateAsync(int hostId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var round = await GuessingRoundQueries.LoadDashboardRoundAsync(db, hostId, ct);
        var votes = ImmutableArray<GuessVoteView>.Empty;
        if (round is not null)
        {
            var voteRows = await db
                .Votes.AsNoTracking()
                .Where(x => x.GuessRoundId == round.Id)
                .OrderByDescending(x => x.GuessedAtUtc)
                .Select(x => new GuessVoteView(x.Login, x.GuessName, x.GuessedAtUtc))
                .ToArrayAsync(ct);
            votes = voteRows.ToImmutableArray();
        }

        var profileId =
            round?.ProfileId ?? await GuessingProfileQueries.DefaultProfileIdAsync(db, hostId, ct);
        var options = await db
            .GuessOptions.AsNoTracking()
            .Where(x => x.GuessRoundProfileId == profileId)
            .OrderBy(x => x.Name)
            .Select(x => new GuessOptionEditor { Name = x.Name, ReplyText = x.ReplyText })
            .ToListAsync(ct);

        return new GuessingDashboardState
        {
            CurrentRound = round,
            Votes = votes,
            Options = options,
            Profiles = await LoadProfileSummariesAsync(db, hostId, ct),
        };
    }

    private static async Task<ImmutableArray<GuessRoundProfileSummary>> LoadProfileSummariesAsync(
        BlokeBotDbContext db,
        int hostId,
        CancellationToken ct
    )
    {
        var profiles = await db
            .Profiles.AsNoTracking()
            .Where(x => x.HostId == hostId)
            .OrderByDescending(x => x.IsDefault)
            .ThenBy(x => x.Name)
            .Select(x => new GuessRoundProfileSummary(x.Id, x.Name, x.IsDefault))
            .ToArrayAsync(ct);
        return profiles.ToImmutableArray();
    }
}
