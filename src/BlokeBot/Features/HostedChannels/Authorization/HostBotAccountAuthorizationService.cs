using BlokeBot.Features.HostedChannels.Runtime;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BlokeBot.Features.HostedChannels.Authorization;

public sealed class HostBotAccountAuthorizationService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    HostBotAccountOAuthService hostBotOAuth,
    TwitchOAuthApiClient oauth,
    TwitchHelixApiClient helix,
    TwitchTokenStatusService globalTokenStatus,
    HostedChannelChangeNotifier changes,
    IOptions<TwitchBotOptions> options
) : ITwitchBotAccountProvider
{
    private static readonly TimeSpan RefreshSkew = TimeSpan.FromMinutes(1);
    private readonly TwitchBotOptions options = options.Value;

    public async Task<BotAccountAuthorizationStatus> GetStatusAsync(
        int hostId,
        CancellationToken ct
    )
    {
        var required = hostBotOAuth.RequestedScopes();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var settings = await db.HostBotAccountSettings.SingleOrDefaultAsync(
            x => x.HostId == hostId,
            ct
        );

        if (settings is null || !settings.OverrideEnabled)
        {
            return new(
                null,
                settings?.Login,
                settings?.ProfileImageUrl,
                BotAccountAuthorizationState.Disabled,
                required,
                SplitStoredScopes(settings?.AuthorizedScopes).ToArray(),
                required,
                "This channel is using the global bot account."
            );
        }

        var tokenStatus = await GetStoredTokenStatusAsync(db, settings, required, ct);
        if (tokenStatus.AccessToken is { Length: > 0 } accessToken)
            await RefreshProfileMetadataAsync(db, settings, accessToken, ct);

        return ToAuthorizationStatus(settings, tokenStatus);
    }

    public async Task<ActiveBotAccountTokenStatus> GetActiveTokenStatusAsync(
        string channelLogin,
        IEnumerable<string?> requiredScopes,
        CancellationToken ct
    )
    {
        var required = TwitchScopeSet.NormalizeMany(requiredScopes);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var host = await db
            .Hosts.AsNoTracking()
            .Where(x => x.Login == TwitchLogin.Normalize(channelLogin))
            .Select(x => new { x.Id })
            .SingleOrDefaultAsync(ct);

        if (host is not null)
        {
            var settings = await db.HostBotAccountSettings.SingleOrDefaultAsync(
                x => x.HostId == host.Id,
                ct
            );
            if (settings?.OverrideEnabled == true)
            {
                return ActiveBotAccountTokenStatus.FromStatus(
                    settings.Login ?? string.Empty,
                    settings.ProfileImageUrl,
                    await GetStoredTokenStatusAsync(db, settings, required, ct)
                );
            }
        }

        var configuredBotLogin = TwitchLogin.Normalize(options.Identity.BotUsername);
        var globalStatus = await globalTokenStatus.GetUserAccessTokenStatusAsync(required, ct);
        return ActiveBotAccountTokenStatus.FromStatus(configuredBotLogin, null, globalStatus);
    }

    public async ValueTask<TwitchBotAccount> GetBotAccountAsync(
        string channelLogin,
        CancellationToken cancellationToken
    )
    {
        var status = await GetActiveTokenStatusAsync(
            channelLogin,
            options.Identity.Scopes,
            cancellationToken
        );
        if (
            status.State == TwitchTokenStatusState.Ready
            && !string.IsNullOrWhiteSpace(status.AccessToken)
            && status.Validation is not null
        )
        {
            return new(TwitchLogin.Normalize(status.Validation.Login), status.AccessToken);
        }

        if (status.State == TwitchTokenStatusState.Unavailable)
        {
            throw new TwitchAccessTokenUnavailableException(
                TwitchAccessTokenUnavailableReason.MissingRefreshToken,
                TwitchAccessTokenUnavailableException.MissingRefreshTokenMessage
            );
        }

        throw new InvalidOperationException(
            $"Bot account authorization is not ready for #{channelLogin}."
        );
    }

    public async Task<bool> CanAuthorizeAsync(int hostId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var settings = await db.HostBotAccountSettings.SingleOrDefaultAsync(
            x => x.HostId == hostId,
            ct
        );
        return settings?.OverrideEnabled == true;
    }

    public async Task SetOverrideEnabledAsync(int hostId, bool enabled, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var settings = await EnsureSettingsAsync(db, hostId, ct);
        if (settings is null)
            return;

        settings.OverrideEnabled = enabled;
        settings.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await changes.NotifyChangedAsync();
    }

    public async Task<BotAccountAuthorizationResult> AuthorizeAsync(
        int hostId,
        HostBotAccountAuthorizationGrant grant,
        CancellationToken ct
    )
    {
        var missingScopes = TwitchScopeSet.Missing(grant.Scopes, hostBotOAuth.RequestedScopes());
        if (missingScopes.Length > 0)
        {
            return BotAccountAuthorizationResult.Failure(
                "Bot account authorization is missing configured permissions.",
                missingScopes
            );
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var settings = await EnsureSettingsAsync(db, hostId, ct);
        if (settings is null)
            return BotAccountAuthorizationResult.Failure("Hosted channel was not found.");

        if (!settings.OverrideEnabled)
            return BotAccountAuthorizationResult.Failure(
                "Enable bot override before authorizing it."
            );

        settings.AccessToken = grant.Token.AccessToken;
        settings.AuthorizedAtUtc = DateTime.UtcNow;
        settings.AuthorizedScopes = TwitchScopeSet.Format(grant.Scopes);
        settings.DisplayName = grant.DisplayName.Trim();
        settings.ExpiresAtUtc = grant.Token.ExpiresAtUtc;
        settings.Login = grant.Login.Value;
        settings.ProfileImageUrl = string.IsNullOrWhiteSpace(grant.ProfileImageUrl)
            ? null
            : grant.ProfileImageUrl.Trim();
        settings.RefreshToken = grant.Token.RefreshToken;
        settings.TwitchUserId = grant.UserId;
        settings.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await changes.NotifyChangedAsync();

        return BotAccountAuthorizationResult.Success("Bot account authorization is current.");
    }

    public async Task ClearAsync(int hostId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var settings = await db.HostBotAccountSettings.SingleOrDefaultAsync(
            x => x.HostId == hostId,
            ct
        );
        if (settings is null)
            return;

        ClearAuthorization(settings);
        settings.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await changes.NotifyChangedAsync();
    }

    private async Task<HostBotAccountSettings?> EnsureSettingsAsync(
        BlokeBotDbContext db,
        int hostId,
        CancellationToken ct
    )
    {
        var settings = await db.HostBotAccountSettings.SingleOrDefaultAsync(
            x => x.HostId == hostId,
            ct
        );
        if (settings is not null)
            return settings;

        if (!await db.Hosts.AnyAsync(x => x.Id == hostId, ct))
            return null;

        settings = new HostBotAccountSettings { HostId = hostId, UpdatedAtUtc = DateTime.UtcNow };
        db.HostBotAccountSettings.Add(settings);
        return settings;
    }

    private async Task<TwitchTokenStatus> GetStoredTokenStatusAsync(
        BlokeBotDbContext db,
        HostBotAccountSettings settings,
        IEnumerable<string?> requiredScopes,
        CancellationToken ct
    )
    {
        var required = TwitchScopeSet.NormalizeMany(requiredScopes);
        if (string.IsNullOrWhiteSpace(settings.RefreshToken))
            return Unavailable(required);

        var accessToken = settings.AccessToken;
        if (string.IsNullOrWhiteSpace(accessToken) || TokenExpiresSoon(settings))
        {
            if (!await RefreshTokenAsync(db, settings, ct))
                return Invalid(required);

            accessToken = settings.AccessToken;
        }

        if (string.IsNullOrWhiteSpace(accessToken))
            return Unavailable(required);

        var validation = await oauth.ValidateTokenAsync(accessToken, ct);
        if (validation is null)
        {
            if (
                !await RefreshTokenAsync(db, settings, ct)
                || string.IsNullOrWhiteSpace(settings.AccessToken)
            )
                return Invalid(required);

            accessToken = settings.AccessToken;
            validation = await oauth.ValidateTokenAsync(accessToken, ct);
            if (validation is null)
                return Invalid(accessToken, required);
        }

        var granted = TwitchScopeSet.NormalizeMany(validation.Scopes);
        var missing = TwitchScopeSet.Missing(granted, required);
        settings.AuthorizedScopes = TwitchScopeSet.Format(granted);
        settings.Login = validation.Login;
        settings.TwitchUserId = validation.UserId;
        settings.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return new(
            missing.Length == 0
                ? TwitchTokenStatusState.Ready
                : TwitchTokenStatusState.MissingScopes,
            accessToken,
            validation,
            required,
            granted,
            missing
        );
    }

    private async Task<bool> RefreshTokenAsync(
        BlokeBotDbContext db,
        HostBotAccountSettings settings,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(settings.RefreshToken))
            return false;

        try
        {
            var refreshed = await oauth.RefreshAsync(
                options.Identity.ClientId,
                options.Identity.ClientSecret,
                settings.RefreshToken,
                ct
            );
            settings.AccessToken = refreshed.AccessToken;
            settings.RefreshToken = refreshed.RefreshToken;
            settings.ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(refreshed.ExpiresIn);
            settings.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    private async Task RefreshProfileMetadataAsync(
        BlokeBotDbContext db,
        HostBotAccountSettings settings,
        string accessToken,
        CancellationToken ct
    )
    {
        var user = await helix.GetCurrentUserAsync(
            new TwitchHelixRequestContext(options.Identity.ClientId, accessToken),
            ct
        );
        if (user is null)
            return;

        settings.DisplayName = string.IsNullOrWhiteSpace(user.DisplayName)
            ? settings.DisplayName
            : user.DisplayName;
        settings.ProfileImageUrl = string.IsNullOrWhiteSpace(user.ProfileImageUrl)
            ? settings.ProfileImageUrl
            : user.ProfileImageUrl;
        settings.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private static BotAccountAuthorizationStatus ToAuthorizationStatus(
        HostBotAccountSettings settings,
        TwitchTokenStatus status
    ) =>
        status.State switch
        {
            TwitchTokenStatusState.Unavailable => new(
                null,
                settings.Login,
                settings.ProfileImageUrl,
                BotAccountAuthorizationState.NotAuthorized,
                status.RequiredScopes,
                status.GrantedScopes,
                status.MissingScopes,
                "Custom bot account authorization has not been completed."
            ),
            TwitchTokenStatusState.Invalid => new(
                null,
                settings.Login,
                settings.ProfileImageUrl,
                BotAccountAuthorizationState.NotAuthorized,
                status.RequiredScopes,
                status.GrantedScopes,
                status.MissingScopes,
                "Custom bot account authorization could not be verified."
            ),
            TwitchTokenStatusState.Unknown => new(
                null,
                settings.Login,
                settings.ProfileImageUrl,
                BotAccountAuthorizationState.Unknown,
                status.RequiredScopes,
                status.GrantedScopes,
                status.MissingScopes,
                "Custom bot account authorization status could not be checked."
            ),
            TwitchTokenStatusState.MissingScopes => new(
                null,
                status.Validation?.Login ?? settings.Login,
                settings.ProfileImageUrl,
                BotAccountAuthorizationState.MissingScopes,
                status.RequiredScopes,
                status.GrantedScopes,
                status.MissingScopes,
                "Custom bot account authorization is missing configured permissions."
            ),
            _ => new(
                null,
                status.Validation?.Login ?? settings.Login,
                settings.ProfileImageUrl,
                BotAccountAuthorizationState.Ready,
                status.RequiredScopes,
                status.GrantedScopes,
                [],
                "Custom bot account authorization is current."
            ),
        };

    private static bool TokenExpiresSoon(HostBotAccountSettings settings) =>
        settings.ExpiresAtUtc is null
        || settings.ExpiresAtUtc <= DateTimeOffset.UtcNow.Add(RefreshSkew);

    private static void ClearAuthorization(HostBotAccountSettings settings)
    {
        settings.AccessToken = null;
        settings.AuthorizedAtUtc = null;
        settings.AuthorizedScopes = null;
        settings.DisplayName = null;
        settings.ExpiresAtUtc = null;
        settings.Login = null;
        settings.ProfileImageUrl = null;
        settings.RefreshToken = null;
        settings.TwitchUserId = null;
    }

    private static IEnumerable<string> SplitStoredScopes(string? scopes) =>
        (scopes ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private static TwitchTokenStatus Unavailable(string[] required) =>
        new(TwitchTokenStatusState.Unavailable, null, null, required, [], required);

    private static TwitchTokenStatus Invalid(string[] required) =>
        new(TwitchTokenStatusState.Invalid, null, null, required, [], required);

    private static TwitchTokenStatus Invalid(string accessToken, string[] required) =>
        new(TwitchTokenStatusState.Invalid, accessToken, null, required, [], required);
}
