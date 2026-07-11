using BlokeBot.Identity;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Features.HostedChannels.Runtime;

public sealed class HostedChannelRuntimeLifecycleService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    HostedChannelChangeNotifier changes
)
{
    public async Task MarkStartedAsync(string channel, CancellationToken ct)
    {
        var normalized = LoginName.Parse(channel);
        if (normalized.IsEmpty)
            return;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var host = await db.Hosts.SingleOrDefaultAsync(x => x.Login == normalized.Value, ct);
        if (host?.BotRuntimeState is not BotChannelRuntimeState.Starting)
            return;

        host.BotRuntimeState = BotChannelRuntimeState.Started;
        host.BotRuntimeStateChangedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await changes.NotifyChangedAsync(ct);
    }

    public async Task MarkStoppedAsync(string channel, CancellationToken ct)
    {
        var normalized = LoginName.Parse(channel);
        if (normalized.IsEmpty)
            return;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var host = await db.Hosts.SingleOrDefaultAsync(x => x.Login == normalized.Value, ct);
        if (host is null || host.BotRuntimeState is BotChannelRuntimeState.Stopped)
            return;

        host.BotRuntimeState = BotChannelRuntimeState.Stopped;
        host.BotRuntimeStateChangedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await changes.NotifyChangedAsync(ct);
    }
}
