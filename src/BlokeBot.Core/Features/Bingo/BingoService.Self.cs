using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Bingo;

public sealed partial class BingoService
{
    public async Task<BingoSelfCardOutcome> GetSelfCardAsync(
        int hostId,
        string twitchUserId,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await FeatureEnabledAsync(db, hostId, ct))
        {
            return new BingoSelfCardOutcome.FeatureDisabled();
        }
        var card = await db
            .BingoCards.AsNoTracking()
            .Where(value =>
                value.HostId == hostId
                && value.Game!.HostId == hostId
                && value.Game.Status == BingoGameStatus.Issued
                && value.Participants.Any(participant =>
                    participant.HostId == hostId && participant.TwitchUserId == twitchUserId
                )
            )
            .Select(value => new BingoSelfCard(
                value.AssignmentName,
                value.Marks.Count(mark => mark.IsActive),
                value.Game!.Dimension * value.Game.Dimension
            ))
            .SingleOrDefaultAsync(ct);
        return card is null
            ? new BingoSelfCardOutcome.NotJoined()
            : new BingoSelfCardOutcome.Available(card);
    }
}
