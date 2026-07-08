using BlokeBot.Eventing;
using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Hosts;

public sealed class BotHostRemovalService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    EventBus<AppEventKind> events
)
{
    public async Task<bool> RemoveAsync(int hostId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var removed = await db.Hosts.Where(host => host.Id == hostId).ExecuteDeleteAsync(ct);
        if (removed == 0)
            return false;

        await transaction.CommitAsync(ct);
        await events.PublishAsync(AppEventKind.HostedChannelsChanged);
        return true;
    }
}
