using BlokeBot.Features.Guessing.Guesses;
using BlokeBot.Features.Guessing.Profiles;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Features.Guessing.Rounds;

public sealed class GuessingDashboardService(IDbContextFactory<BlokeBotDbContext> dbFactory)
{
    public async Task<GuessingDashboardState> LoadStateAsync(int hostId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var round = await UnresolvedRoundQuery(db, hostId)
            .Include(x => x.GuessRoundProfile)
            .AsNoTracking()
            .FirstOrDefaultAsync(ct);
        var votes = round is null
            ? []
            : await db
                .Votes.AsNoTracking()
                .Where(x => x.GuessRoundId == round.Id)
                .OrderByDescending(x => x.GuessedAtUtc)
                .Select(x => new GuessVoteView(x.Login, x.GuessName, x.GuessedAtUtc))
                .ToListAsync(ct);

        var profileId = round?.GuessRoundProfileId ?? await DefaultProfileIdAsync(db, hostId, ct);
        var options = await db
            .GuessOptions.AsNoTracking()
            .Where(x => x.GuessRoundProfileId == profileId)
            .OrderBy(x => x.Name)
            .Select(x => new GuessOptionEditor { Name = x.Name, ReplyText = x.ReplyText })
            .ToListAsync(ct);

        return new GuessingDashboardState
        {
            CurrentRound = round is null
                ? null
                : new GuessRoundView(
                    round.Id,
                    round.GuessRoundProfileId,
                    round.GuessRoundProfile?.Name ?? string.Empty,
                    ParseRoundStatus(round.Status),
                    round.StartedAtUtc,
                    round.ClosedAtUtc,
                    round.WinningName
                ),
            Votes = votes,
            Options = options,
            Profiles = await LoadProfileSummariesAsync(db, hostId, ct),
        };
    }

    private static async Task<int> DefaultProfileIdAsync(
        BlokeBotDbContext db,
        int hostId,
        CancellationToken ct
    ) =>
        await db
            .Profiles.Where(x => x.HostId == hostId && x.IsDefault)
            .Select(x => x.Id)
            .FirstAsync(ct);

    private static async Task<List<GuessRoundProfileSummary>> LoadProfileSummariesAsync(
        BlokeBotDbContext db,
        int hostId,
        CancellationToken ct
    ) =>
        await db
            .Profiles.AsNoTracking()
            .Where(x => x.HostId == hostId)
            .OrderByDescending(x => x.IsDefault)
            .ThenBy(x => x.Name)
            .Select(x => new GuessRoundProfileSummary(x.Id, x.Name, x.IsDefault))
            .ToListAsync(ct);

    private static GuessRoundStatus ParseRoundStatus(string value) =>
        Enum.TryParse<GuessRoundStatus>(value, ignoreCase: true, out var parsed)
            ? parsed
            : GuessRoundStatus.Closed;

    private static string Store(GuessRoundStatus status) => status.ToString();

    private static IQueryable<GuessRound> UnresolvedRoundQuery(BlokeBotDbContext db, int hostId) =>
        db
            .Rounds.Where(x => x.GuessRoundProfile != null && x.GuessRoundProfile.HostId == hostId)
            .Where(x =>
                x.Status == Store(GuessRoundStatus.Open)
                || x.Status == Store(GuessRoundStatus.Closed)
            )
            .OrderByDescending(x => x.StartedAtUtc);
}
