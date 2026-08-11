using System.Collections.Immutable;
using System.Diagnostics;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Eventing;
using BlokeBot.Functional;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.HostedChannels.Authorization;

public interface IHostBroadcasterTokenStatusProvider
{
    Task<TokenStatus> GetTokenStatusAsync(
        int hostId,
        IEnumerable<string?> requiredScopes,
        CancellationToken ct
    );

    IO<BotAccount, AccessTokenUnavailableReason> GetBroadcasterAccount(string channelLogin);
}

public sealed class HostBroadcasterAuthorizationService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    IHostBotAccountTokenProtector tokenProtector,
    OAuthTransport transport,
    BotSettings settings,
    HostedChannelChangeNotifier changes
) : IHostBroadcasterTokenStatusProvider, IBroadcasterAccountProvider
{
    public static readonly string[] MilestoneScopes =
    [
        "channel:read:polls",
        "channel:manage:polls",
        "clips:edit",
        "channel:manage:broadcast",
        "channel:read:redemptions",
        "channel:manage:redemptions",
        "channel:read:predictions",
        "channel:manage:predictions",
        "channel:read:subscriptions",
        "bits:read",
        "channel:read:hype_train",
    ];

    public static readonly string[] RaidManagementScopes =
    [
        .. MilestoneScopes,
        Scopes.ChannelManageRaids,
    ];

    public async Task<HostBroadcasterAuthorizationOutcome> AuthorizeAsync(
        int hostId,
        HostBotAccountAuthorizationGrant grant,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var host = await db.Hosts.SingleOrDefaultAsync(x => x.Id == hostId, ct);
        if (host is null)
        {
            return new HostBroadcasterAuthorizationOutcome.HostNotFound();
        }
        if (!string.Equals(host.TwitchUserId, grant.UserId, StringComparison.Ordinal))
        {
            return new HostBroadcasterAuthorizationOutcome.GrantMismatch();
        }
        var missing = ScopeSet.Missing(grant.Scopes, MilestoneScopes);
        if (missing.Length > 0)
        {
            return new HostBroadcasterAuthorizationOutcome.MissingScopes(missing);
        }
        var protectedToken = tokenProtector.Protect(hostId, grant.Token);
        return await protectedToken.Match(
            async payload =>
            {
                var authorization = await db.HostBroadcasterAuthorizations.SingleOrDefaultAsync(
                    x => x.HostId == hostId,
                    ct
                );
                if (authorization is null)
                {
                    authorization = new HostBroadcasterAuthorization { HostId = hostId };
                    _ = db.HostBroadcasterAuthorizations.Add(authorization);
                }
                authorization.ProtectedTokenPayload = payload;
                authorization.TwitchUserId = grant.UserId;
                authorization.Login = grant.Login.Value;
                authorization.AuthorizedScopes = ScopeSet.Format(grant.Scopes);
                authorization.AuthorizedAtUtc = DateTime.UtcNow;
                authorization.UpdatedAtUtc = DateTime.UtcNow;
                _ = await db.SaveChangesAsync(ct);
                _ = await changes.NotifyChangedAsync(ct);
                return (HostBroadcasterAuthorizationOutcome)
                    new HostBroadcasterAuthorizationOutcome.Authorized();
            },
            _ =>
                Task.FromResult<HostBroadcasterAuthorizationOutcome>(
                    new HostBroadcasterAuthorizationOutcome.ProtectionUnavailable()
                )
        );
    }

    public async Task<TokenStatus> GetTokenStatusAsync(
        int hostId,
        IEnumerable<string?> requiredScopes,
        CancellationToken ct
    )
    {
        var required = ImmutableArray.CreateRange(ScopeSet.NormalizeMany(requiredScopes));
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var host = await db.Hosts.SingleOrDefaultAsync(x => x.Id == hostId, ct);
        var authorization = await db.HostBroadcasterAuthorizations.SingleOrDefaultAsync(
            x => x.HostId == hostId,
            ct
        );
        if (host is null || authorization?.ProtectedTokenPayload is null)
        {
            return new TokenStatus.Unavailable(
                AccessTokenUnavailableReason.MissingRefreshToken,
                required
            );
        }
        var unprotected = tokenProtector.Unprotect(hostId, authorization.ProtectedTokenPayload);
        return await unprotected.Match(
            payload => ValidateOrRefreshAsync(db, host, authorization, payload, required, ct),
            _ =>
                Task.FromResult<TokenStatus>(
                    new TokenStatus.Unavailable(
                        AccessTokenUnavailableReason.CredentialProtectionUnavailable,
                        required
                    )
                )
        );
    }

    public async Task<HostBroadcasterAuthorizationClearOutcome> ClearAsync(
        int hostId,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var authorization = await db.HostBroadcasterAuthorizations.SingleOrDefaultAsync(
            value => value.HostId == hostId,
            ct
        );
        if (authorization is null)
        {
            return new HostBroadcasterAuthorizationClearOutcome.AlreadyDisconnected();
        }

        _ = db.HostBroadcasterAuthorizations.Remove(authorization);
        _ = await db.SaveChangesAsync(ct);

        try
        {
            return await changes.NotifyChangedAsync(ct) switch
            {
                ObserverFanOutOutcome.AllSucceeded =>
                    new HostBroadcasterAuthorizationClearOutcome.Cleared(),
                ObserverFanOutOutcome.CompletedWithFailures failed =>
                    new HostBroadcasterAuthorizationClearOutcome.ClearedWithNotificationFailures(
                        failed.Failures.Count
                    ),
                _ => throw new InvalidOperationException("Unknown observer fan-out outcome."),
            };
        }
        catch (ObserverFanOutEscalationException escalation)
        {
            return new HostBroadcasterAuthorizationClearOutcome.ClearedWithNotificationEscalation(
                escalation.Failures.Count,
                escalation.HandlingFailures.Count
            );
        }
    }

    public IO<BotAccount, AccessTokenUnavailableReason> GetBroadcasterAccount(
        string channelLogin
    ) =>
        IO<BotAccount, AccessTokenUnavailableReason>.Create(async ct =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var host = await db
                .Hosts.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Login == Login.Normalize(channelLogin), ct);
            if (host is null)
            {
                return Result<BotAccount, AccessTokenUnavailableReason>.Error(
                    AccessTokenUnavailableReason.BroadcasterAuthorizationUnavailable
                );
            }
            var status = await GetTokenStatusAsync(host.Id, MilestoneScopes, ct);
            return status.Match(
                _ =>
                    Result<BotAccount, AccessTokenUnavailableReason>.Error(
                        AccessTokenUnavailableReason.BroadcasterAuthorizationUnavailable
                    ),
                unavailable =>
                    Result<BotAccount, AccessTokenUnavailableReason>.Error(unavailable.Reason),
                _ =>
                    Result<BotAccount, AccessTokenUnavailableReason>.Error(
                        AccessTokenUnavailableReason.BroadcasterAuthorizationUnavailable
                    ),
                _ =>
                    Result<BotAccount, AccessTokenUnavailableReason>.Error(
                        AccessTokenUnavailableReason.BroadcasterAuthorizationUnavailable
                    ),
                ready =>
                    Result<BotAccount, AccessTokenUnavailableReason>.Success(
                        new BotAccount(ready.Validation.Login, ready.AccessToken)
                    )
            );
        });

    private async Task<TokenStatus> ValidateOrRefreshAsync(
        BlokeBotDbContext db,
        BotHost host,
        HostBroadcasterAuthorization authorization,
        HostBotAccountTokenPayload payload,
        ImmutableArray<string> required,
        CancellationToken ct
    )
    {
        if (payload.ExpiresAtUtc <= DateTimeOffset.UtcNow.AddMinutes(1))
        {
            var refresh = await transport.RefreshCompleteTokenSetAsync(
                settings.Identity.ClientId,
                settings.Identity.ClientSecret,
                payload.RefreshToken,
                ct
            );
            if (string.IsNullOrWhiteSpace(refresh.RefreshToken))
            {
                return new TokenStatus.Invalid(required);
            }
            payload = new HostBotAccountTokenPayload(
                refresh.AccessToken,
                refresh.RefreshToken,
                DateTimeOffset.UtcNow.AddSeconds(refresh.ExpiresIn)
            );
            var protectedPayload = tokenProtector.Protect(host.Id, payload);
            byte[]? storedPayload = null;
            _ = protectedPayload.Match(value => storedPayload = value, _ => storedPayload = null);
            if (storedPayload is null)
            {
                return new TokenStatus.Unavailable(
                    AccessTokenUnavailableReason.CredentialProtectionUnavailable,
                    required
                );
            }
            authorization.ProtectedTokenPayload = storedPayload;
        }
        var validated = await transport.ValidateTokenAsync(payload.AccessToken, ct);
        if (
            validated is not TokenValidationOutcome.Validated valid
            || !string.Equals(valid.Validation.UserId, host.TwitchUserId, StringComparison.Ordinal)
        )
        {
            return new TokenStatus.Invalid(required);
        }
        var granted = ImmutableArray.CreateRange(ScopeSet.NormalizeMany(valid.Validation.Scopes));
        var missing = ImmutableArray.CreateRange(ScopeSet.Missing(granted, required));
        authorization.TwitchUserId = valid.Validation.UserId;
        authorization.Login = valid.Validation.Login;
        authorization.AuthorizedScopes = ScopeSet.Format(granted);
        authorization.UpdatedAtUtc = DateTime.UtcNow;
        _ = await db.SaveChangesAsync(ct);
        return missing.IsEmpty
            ? new TokenStatus.Ready(payload.AccessToken, valid.Validation, required, granted)
            : new TokenStatus.MissingScopes(
                payload.AccessToken,
                valid.Validation,
                required,
                granted,
                missing
            );
    }
}

public abstract record HostBroadcasterAuthorizationOutcome
{
    private HostBroadcasterAuthorizationOutcome() { }

    public sealed record Authorized : HostBroadcasterAuthorizationOutcome;

    public sealed record HostNotFound : HostBroadcasterAuthorizationOutcome;

    public sealed record GrantMismatch : HostBroadcasterAuthorizationOutcome;

    public sealed record MissingScopes(IReadOnlyList<string> Scopes)
        : HostBroadcasterAuthorizationOutcome;

    public sealed record ProtectionUnavailable : HostBroadcasterAuthorizationOutcome;
}

public abstract record HostBroadcasterAuthorizationClearOutcome
{
    private HostBroadcasterAuthorizationClearOutcome() { }

    public TResult Match<TResult>(
        Func<AlreadyDisconnected, TResult> alreadyDisconnected,
        Func<Cleared, TResult> cleared,
        Func<ClearedWithNotificationFailures, TResult> failed,
        Func<ClearedWithNotificationEscalation, TResult> escalated
    ) =>
        this switch
        {
            AlreadyDisconnected value => alreadyDisconnected(value),
            Cleared value => cleared(value),
            ClearedWithNotificationFailures value => failed(value),
            ClearedWithNotificationEscalation value => escalated(value),
            _ => throw new UnreachableException(),
        };

    public sealed record AlreadyDisconnected : HostBroadcasterAuthorizationClearOutcome;

    public sealed record Cleared : HostBroadcasterAuthorizationClearOutcome;

    public sealed record ClearedWithNotificationFailures(int FailureCount)
        : HostBroadcasterAuthorizationClearOutcome;

    public sealed record ClearedWithNotificationEscalation(
        int ObserverFailureCount,
        int HandlingFailureCount
    ) : HostBroadcasterAuthorizationClearOutcome;
}
