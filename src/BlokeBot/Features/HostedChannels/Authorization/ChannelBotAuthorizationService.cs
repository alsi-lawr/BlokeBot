using BlokeBot.Features.HostedChannels.Runtime;
using BlokeBot.Functional;
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

    public IO<ChannelBotAuthorizationOutcome, Never> Authorize(
        int hostId,
        ChannelBotAuthorizationGrant grant
    )
    {
        return IO<ChannelBotAuthorizationOutcome, Never>.Create(async ct =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var host = await db.Hosts.SingleOrDefaultAsync(x => x.Id == hostId, ct);
            if (host is null)
            {
                return Success(new ChannelBotAuthorizationOutcome.HostNotFound());
            }

            if (!GrantMatchesHost(host.TwitchUserId, host.Login, grant))
            {
                return Success(new ChannelBotAuthorizationOutcome.GrantMismatch());
            }

            var missingScopes = MissingRequiredScopes(grant.Scopes);
            if (missingScopes.Length > 0)
            {
                return Success(new ChannelBotAuthorizationOutcome.MissingScopes(missingScopes));
            }

            host.ChannelBotAuthorizedAtUtc = DateTime.UtcNow;
            host.ChannelBotAuthorizedScopes = ScopeSet.Format(grant.Scopes);
            await db.SaveChangesAsync(ct);
            await changes.NotifyChangedAsync(ct);
            return Success(new ChannelBotAuthorizationOutcome.Authorized());
        });
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
        return ScopeSet.Missing(grantedScopes, oauth.RequestedScopes());
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

    private static Result<ChannelBotAuthorizationOutcome, Never> Success(
        ChannelBotAuthorizationOutcome outcome
    )
    {
        return Result<ChannelBotAuthorizationOutcome, Never>.Success(outcome);
    }
}

public abstract record ChannelBotAuthorizationOutcome
{
    private ChannelBotAuthorizationOutcome() { }

    public sealed record Authorized : ChannelBotAuthorizationOutcome;

    public sealed record HostNotFound : ChannelBotAuthorizationOutcome;

    public sealed record GrantMismatch : ChannelBotAuthorizationOutcome;

    public sealed record MissingScopes(IReadOnlyList<string> Scopes)
        : ChannelBotAuthorizationOutcome;
}
