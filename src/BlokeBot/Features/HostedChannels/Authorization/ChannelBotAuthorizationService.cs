using BlokeBot.Features.HostedChannels.Runtime;
using BlokeBot.Identity;
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
        {
            return;
        }

        if (IsCurrent(host.ChannelBotAuthorizedAtUtc, host.ChannelBotAuthorizedScopes))
        {
            return;
        }

        host.ChannelBotAuthorizedAtUtc = null;
        host.ChannelBotAuthorizedScopes = null;
        await db.SaveChangesAsync(ct);
        await changes.NotifyChangedAsync(ct);
    }

    public async Task<ChannelBotAuthorizationResult> AuthorizeAsync(
        int hostId,
        ChannelBotAuthorizationGrant grant,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var host = await db.Hosts.SingleOrDefaultAsync(x => x.Id == hostId, ct);
        if (host is null)
        {
            return ChannelBotAuthorizationResult.Failure("Channel setup was not found.");
        }

        if (!GrantMatchesHost(host.TwitchUserId, host.Login, grant))
        {
            return ChannelBotAuthorizationResult.Failure(
                "That Twitch sign-in belongs to a different channel."
            );
        }

        var missingScopes = MissingRequiredScopes(grant.Scopes);
        if (missingScopes.Length > 0)
        {
            return ChannelBotAuthorizationResult.Failure(
                "The bot still needs more Twitch access for this channel.",
                missingScopes
            );
        }

        host.ChannelBotAuthorizedAtUtc = DateTime.UtcNow;
        host.ChannelBotAuthorizedScopes = TwitchScopeSet.Format(grant.Scopes);
        await db.SaveChangesAsync(ct);
        await changes.NotifyChangedAsync(ct);
        return ChannelBotAuthorizationResult.Success("The bot can chat in this channel.");
    }

    public async Task ClearAsync(int hostId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var host = await db.Hosts.SingleOrDefaultAsync(x => x.Id == hostId, ct);
        if (host is null)
        {
            return;
        }

        host.ChannelBotAuthorizedAtUtc = null;
        host.ChannelBotAuthorizedScopes = null;
        await db.SaveChangesAsync(ct);
        await changes.NotifyChangedAsync(ct);
    }

    public bool IsCurrent(DateTime? authorizedAtUtc, string? authorizedScopes)
    {
        return authorizedAtUtc is not null && HasRequiredScopes(authorizedScopes);
    }

    private bool HasRequiredScopes(string? authorizedScopes)
    {
        return MissingRequiredScopes(SplitStoredScopes(authorizedScopes)).Length == 0;
    }

    private string[] MissingRequiredScopes(IEnumerable<string> grantedScopes)
    {
        return TwitchScopeSet.Missing(grantedScopes, oauth.RequestedScopes());
    }

    private static IEnumerable<string> SplitStoredScopes(string? scopes)
    {
        return (scopes ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    private static bool GrantMatchesHost(
        string? hostTwitchUserId,
        string hostLogin,
        ChannelBotAuthorizationGrant grant
    )
    {
        if (!string.IsNullOrWhiteSpace(hostTwitchUserId))
        {
            return string.Equals(hostTwitchUserId, grant.UserId, StringComparison.Ordinal);
        }

        return LoginName.Parse(hostLogin) == grant.Login;
    }
}
