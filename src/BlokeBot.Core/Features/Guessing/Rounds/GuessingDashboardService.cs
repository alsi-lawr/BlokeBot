using System.Collections.Immutable;
using BlokeBot.Core.Features.Guessing.Guesses;
using BlokeBot.Core.Features.Guessing.Profiles;
using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Guessing.Rounds;

public sealed class GuessingDashboardService(IDbContextFactory<BlokeBotDbContext> dbFactory)
{
    internal async Task<GuessingControlState?> ReadControlAsync(
        int hostId,
        int selectedProfileId,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var profile = await db
            .Profiles.AsNoTracking()
            .Where(x => x.HostId == hostId && x.Id == selectedProfileId)
            .Select(x => new GuessRoundProfileSummary(x.Id, x.Revision, x.Name, x.IsDefault))
            .SingleOrDefaultAsync(ct);
        if (profile is null)
        {
            return null;
        }
        var round = await GuessingRoundQueries.LoadDashboardRoundAsync(db, hostId, ct);
        if (round is null)
        {
            return new(profile, null, []);
        }
        var names = await db
            .GuessOptions.AsNoTracking()
            .Where(x => x.GuessRoundProfileId == round.ProfileId)
            .Select(x => x.Name)
            .ToArrayAsync(ct);
        return new(
            profile,
            round,
            names
                .Select(x => GuessAnswerNames.Parse(x).Canonical)
                .OrderBy(x => x.Value, StringComparer.OrdinalIgnoreCase)
                .ToImmutableArray()
        );
    }

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
