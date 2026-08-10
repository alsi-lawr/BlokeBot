using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence;

internal static class BingoAssignmentKeyMigration
{
    internal static async Task ApplyAsync(BlokeBotDbContext db, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var cards = await db
            .BingoCards.Include(value => value.Game!)
                .ThenInclude(value => value.TemplateRevision!)
                    .ThenInclude(value => value.Squares)
            .Where(value =>
                value.Game!.Mode == BingoGameMode.UniquePerViewer
                && value.AssignmentKey.StartsWith(BingoCardAssignmentKey.LegacyUniquePrefix)
            )
            .ToArrayAsync(ct);
        foreach (var card in cards)
        {
            var game = card.Game!;
            card.IssuedLayout = BingoIssuedLayout.Serialize(
                BingoIssuedLayout.Generate(
                    game.Seed,
                    game.TemplateRevisionNumber,
                    game.Dimension,
                    card.AssignmentKey,
                    game.TemplateRevision!.Squares.Select(value => value.Key)
                )
            );
            card.AssignmentKey = BingoCardAssignmentKey.Opaque(card.PublicId);
        }
        if (cards.Length > 0)
        {
            _ = await db.SaveChangesAsync(ct);
        }
        await transaction.CommitAsync(ct);
    }
}
