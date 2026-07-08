using Alsi.TwitchBot;
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
            return;

        if (IsCurrent(host.ChannelBotAuthorizedAtUtc, host.ChannelBotAuthorizedScopes))
            return;

        host.ChannelBotAuthorizedAtUtc = null;
        host.ChannelBotAuthorizedScopes = null;
        await db.SaveChangesAsync(ct);
        await changes.NotifyChangedAsync();
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
            return ChannelBotAuthorizationResult.Failure("Hosted channel was not found.");

        if (!GrantMatchesHost(host.TwitchUserId, host.Login, grant))
        {
            return ChannelBotAuthorizationResult.Failure(
                "The Twitch authorization belongs to a different channel."
            );
        }

        var missingScopes = MissingRequiredScopes(grant.Scopes);
        if (missingScopes.Length > 0)
        {
            return ChannelBotAuthorizationResult.Failure(
                "Channel bot authorization is missing configured permissions.",
                missingScopes
            );
        }

        host.ChannelBotAuthorizedAtUtc = DateTime.UtcNow;
        host.ChannelBotAuthorizedScopes = TwitchScopeSet.Format(grant.Scopes);
        await db.SaveChangesAsync(ct);
        await changes.NotifyChangedAsync();
        return ChannelBotAuthorizationResult.Success("Channel bot authorization is current.");
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

    public bool IsCurrent(DateTime? authorizedAtUtc, string? authorizedScopes) =>
        authorizedAtUtc is not null && HasRequiredScopes(authorizedScopes);

    private bool HasRequiredScopes(string? authorizedScopes) =>
        MissingRequiredScopes(SplitStoredScopes(authorizedScopes)).Length == 0;

    private string[] MissingRequiredScopes(IEnumerable<string> grantedScopes) =>
        TwitchScopeSet.Missing(grantedScopes, oauth.RequestedScopes());

    private static IEnumerable<string> SplitStoredScopes(string? scopes) =>
        (scopes ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private static bool GrantMatchesHost(
        string? hostTwitchUserId,
        string hostLogin,
        ChannelBotAuthorizationGrant grant
    )
    {
        if (!string.IsNullOrWhiteSpace(hostTwitchUserId))
            return string.Equals(hostTwitchUserId, grant.UserId, StringComparison.Ordinal);

        return LoginName.Parse(hostLogin) == grant.Login;
    }
}
