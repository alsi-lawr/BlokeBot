using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Bingo;

internal sealed record BingoSummaryGame(string Name, BingoGameStatus Status);

internal sealed record BingoSummaryWin(string Name, DateTime CompletedAtUtc);

internal sealed record BingoPublicSummary(
    BingoSummaryGame? Game,
    IReadOnlyList<BingoSummaryWin> Wins
);

public sealed partial class BingoService
{
    internal async Task<BingoPublicSummary?> GetPublicSummaryAsync(int hostId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await FeatureEnabledAsync(db, hostId, ct))
        {
            return null;
        }
        // Preserve the public owner's latest-50 game window, without loading card graphs.
        var games = await db
            .BingoGames.AsNoTracking()
            .Where(value => value.HostId == hostId && value.Status != BingoGameStatus.Joining)
            .OrderByDescending(value => value.CreatedAtUtc)
            .Take(50)
            .Select(value => new
            {
                value.Id,
                value.TemplateName,
                value.Status,
            })
            .ToArrayAsync(ct);
        var selected =
            games.FirstOrDefault(value => value.Status == BingoGameStatus.Issued)
            ?? games.FirstOrDefault(value => value.Status == BingoGameStatus.Archived);
        var visibleIds = games
            .Where(value =>
                value.Status == BingoGameStatus.Archived
                || value.Id == (selected == null ? 0 : selected.Id)
            )
            .Select(value => value.Id)
            .ToArray();
        var wins = await db
            .BingoWins.AsNoTracking()
            .Where(value => value.HostId == hostId && visibleIds.Contains(value.GameId))
            .OrderByDescending(value => value.CompletedAtUtc)
            .ThenBy(value => value.Id)
            .Take(5)
            .Select(value => new BingoSummaryWin(value.Game!.TemplateName, value.CompletedAtUtc))
            .ToArrayAsync(ct);
        return new(selected is null ? null : new(selected.TemplateName, selected.Status), wins);
    }
}
