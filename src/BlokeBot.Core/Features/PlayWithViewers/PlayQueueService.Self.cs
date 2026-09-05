using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.PlayWithViewers;

public sealed partial class PlayQueueService
{
    public async Task<PlayQueueResult<PublicPlayQueueEntryView>> GetSelfPositionAsync(
        int hostId,
        string queueSlug,
        string twitchUserId,
        CancellationToken ct
    )
    {
        if (!await FeatureIsEnabledAsync(hostId, ct))
        {
            return Rejected<PublicPlayQueueEntryView>(new PlayQueueRejection.FeatureDisabled());
        }
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var slug = CommunityInput.NormalizeSlug(queueSlug);
        var queue = await db
            .PlayQueues.AsNoTracking()
            .Include(value => value.Fields)
            .SingleOrDefaultAsync(value => value.HostId == hostId && value.Slug == slug, ct);
        if (queue is null)
        {
            return Rejected<PublicPlayQueueEntryView>(
                new PlayQueueRejection.NotFound("Queue not found.")
            );
        }
        var entry = await db
            .PlayQueueEntries.AsNoTracking()
            .Include(value => value.Values)
                .ThenInclude(value => value.Field)
            .Where(value =>
                value.HostId == hostId
                && value.QueueId == queue.Id
                && value.TwitchUserId == twitchUserId
                && (
                    value.Status == PlayQueueEntryStatus.Waiting
                    || value.Status == PlayQueueEntryStatus.AwaitingReady
                    || value.Status == PlayQueueEntryStatus.Ready
                    || value.Status == PlayQueueEntryStatus.Selected
                )
            )
            .SingleOrDefaultAsync(ct);
        return entry is null
            ? Rejected<PublicPlayQueueEntryView>(new PlayQueueRejection.NotJoined())
            : Succeeded(await ToPublicViewAsync(db, queue, entry, ct));
    }
}
