using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.TwitchOperations;

internal static class NativeTwitchReconciliation
{
    public static async Task ReconcileChannelAsync(
        IDbContextFactory<BlokeBotDbContext> dbFactory,
        string channel,
        Func<int, CancellationToken, Task> reconcile,
        CancellationToken cancellationToken,
        bool queryEmptyLogin = false
    )
    {
        var login = Login.Normalize(channel);
        if (!queryEmptyLogin && login.Length == 0)
        {
            return;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var hostId = await db
            .Hosts.Where(host => host.Login == login)
            .Select(host => (int?)host.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (hostId is { } id)
        {
            await reconcile(id, cancellationToken);
        }
    }
}
