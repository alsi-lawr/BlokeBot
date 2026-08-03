using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Hosts;

public sealed class BotHostRemovalService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    HostedChannelChangeNotifier changes
)
{
    public async Task<bool> RemoveAsync(int hostId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var removed = await db.Hosts.Where(host => host.Id == hostId).ExecuteDeleteAsync(ct);
        if (removed == 0)
        {
            return false;
        }

        await transaction.CommitAsync(ct);
        _ = await changes.NotifyChangedAsync(ct);
        return true;
    }
}
