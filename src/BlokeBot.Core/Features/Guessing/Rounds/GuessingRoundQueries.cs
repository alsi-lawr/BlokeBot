using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Guessing.Rounds;

internal sealed record GuessRoundReference(int Id, int ProfileId, GuessRoundLifecycle Lifecycle);

internal static class GuessingRoundQueries
{
    public static async Task<GuessRound?> LoadTrackedUnresolvedAsync(
        BlokeBotDbContext db,
        int hostId,
        CancellationToken ct
    )
    {
        var round = await Unresolved(db, hostId).FirstOrDefaultAsync(ct);
        if (round is not null)
        {
            _ = ToLifecycle(round);
        }

        return round;
    }

    public static async Task<GuessRound?> LoadTrackedOpenAsync(
        BlokeBotDbContext db,
        int hostId,
        CancellationToken ct
    )
    {
        var round = await db
            .Rounds.Where(x => x.HostId == hostId && x.Status == GuessRoundStatus.Open)
            .OrderByDescending(x => x.StartedAtUtc)
            .FirstOrDefaultAsync(ct);
        if (round is not null)
        {
            _ = ToLifecycle(round);
        }

        return round;
    }

    public static async Task<GuessRoundReference?> LoadUnresolvedAsync(
        BlokeBotDbContext db,
        int hostId,
        CancellationToken ct
    )
    {
        var round = await Unresolved(db, hostId)
            .AsNoTracking()
            .Select(x => new
            {
                x.Id,
                x.GuessRoundProfileId,
                x.Status,
                x.StartedAtUtc,
                x.ClosedAtUtc,
                x.WinningName,
            })
            .FirstOrDefaultAsync(ct);
        return round is null
            ? null
            : new GuessRoundReference(
                round.Id,
                round.GuessRoundProfileId,
                GuessRoundLifecycle.FromPersistence(
                    round.Status,
                    round.StartedAtUtc,
                    round.ClosedAtUtc,
                    round.WinningName
                )
            );
    }

    public static async Task<GuessRoundView?> LoadDashboardRoundAsync(
        BlokeBotDbContext db,
        int hostId,
        CancellationToken ct
    )
    {
        var round = await Unresolved(db, hostId)
            .AsNoTracking()
            .Select(x => new
            {
                x.Id,
                x.GuessRoundProfileId,
                ProfileName = x.GuessRoundProfile == null ? string.Empty : x.GuessRoundProfile.Name,
                x.Status,
                x.StartedAtUtc,
                x.ClosedAtUtc,
                x.WinningName,
            })
            .FirstOrDefaultAsync(ct);
        return round is null
            ? null
            : new GuessRoundView(
                round.Id,
                round.GuessRoundProfileId,
                round.ProfileName,
                GuessRoundLifecycle.FromPersistence(
                    round.Status,
                    round.StartedAtUtc,
                    round.ClosedAtUtc,
                    round.WinningName
                )
            );
    }

    public static Task<bool> HasUnresolvedAsync(
        BlokeBotDbContext db,
        int hostId,
        CancellationToken ct
    )
    {
        return Unresolved(db, hostId).AnyAsync(ct);
    }

    private static IQueryable<GuessRound> Unresolved(BlokeBotDbContext db, int hostId)
    {
        return db
            .Rounds.Where(x => x.HostId == hostId)
            .Where(x => x.Status == GuessRoundStatus.Open || x.Status == GuessRoundStatus.Closed)
            .OrderByDescending(x => x.StartedAtUtc);
    }

    private static GuessRoundLifecycle ToLifecycle(GuessRound round)
    {
        return GuessRoundLifecycle.FromPersistence(
            round.Status,
            round.StartedAtUtc,
            round.ClosedAtUtc,
            round.WinningName
        );
    }
}
