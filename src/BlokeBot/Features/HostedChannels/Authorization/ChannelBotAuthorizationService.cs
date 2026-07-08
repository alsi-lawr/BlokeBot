using BlokeBot.Features.HostedChannels.Runtime;
using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Features.HostedChannels.Authorization;

public sealed class ChannelBotAuthorizationService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    HostedChannelChangeNotifier changes,
    ChannelBotOAuthService oauth
)
{
    public async Task ClearIfScopesStaleAsync(int hostId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var host = await db.Hosts.SingleOrDefaultAsync(x => x.Id == hostId, ct);
        if (host is null || host.ChannelBotAuthorizedAtUtc is null)
            return;

        var configuredScopes = ChannelBotOAuthService.FormatScopes(oauth.RequestedScopes());
        if (
            string.Equals(
                host.ChannelBotAuthorizedScopes,
                configuredScopes,
                StringComparison.Ordinal
            )
        )
            return;

        host.ChannelBotAuthorizedAtUtc = null;
        host.ChannelBotAuthorizedScopes = null;
        await db.SaveChangesAsync(ct);
        await changes.NotifyChangedAsync();
    }

    public async Task MarkAuthorizedAsync(int hostId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var host = await db.Hosts.SingleOrDefaultAsync(x => x.Id == hostId, ct);
        if (host is null)
            return;

        host.ChannelBotAuthorizedAtUtc = DateTime.UtcNow;
        host.ChannelBotAuthorizedScopes = ChannelBotOAuthService.FormatScopes(
            oauth.RequestedScopes()
        );
        await db.SaveChangesAsync(ct);
        await changes.NotifyChangedAsync();
    }

    public async Task ClearAsync(int hostId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var host = await db.Hosts.SingleOrDefaultAsync(x => x.Id == hostId, ct);
        if (host is null)
            return;

        host.ChannelBotAuthorizedAtUtc = null;
        host.ChannelBotAuthorizedScopes = null;
        await db.SaveChangesAsync(ct);
        await changes.NotifyChangedAsync();
    }
}
