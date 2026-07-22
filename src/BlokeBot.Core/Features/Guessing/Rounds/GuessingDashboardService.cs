using System.Collections.Immutable;
using BlokeBot.Core.Features.Guessing.Guesses;
using BlokeBot.Core.Features.Guessing.Profiles;
using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Guessing.Rounds;

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

        var profileId = round?.ProfileId ?? await db.Profiles.LoadDefaultProfileIdAsync(hostId, ct);
        var optionRows = await db
            .GuessOptions.AsNoTracking()
            .Where(x => x.GuessRoundProfileId == profileId)
            .Select(x => new { x.Name, x.ReplyText })
            .ToListAsync(ct);
        var options = optionRows
            .Select(option => new GuessOptionEditor
            {
                Name = GuessAnswerNames.Parse(option.Name).Canonical.Value,
                ReplyText = option.ReplyText,
            })
            .OrderBy(option => option.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

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
            .Select(x => new GuessRoundProfileSummary(x.Id, x.Revision, x.Name, x.IsDefault))
            .ToArrayAsync(ct);
        return profiles.ToImmutableArray();
    }
}
