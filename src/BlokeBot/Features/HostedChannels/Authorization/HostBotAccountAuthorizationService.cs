using System.Collections.Immutable;
using BlokeBot.Features.HostedChannels.Runtime;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Features.HostedChannels.Authorization;

public sealed class HostBotAccountAuthorizationService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    HostBotAccountOAuthService hostBotOAuth,
    TwitchOAuthApiClient oauth,
    HelixClient helix,
    ITwitchTokenStatusSource globalTokenStatus,
    HostedChannelChangeNotifier changes,
    TwitchBotSettings botSettings
) : ITwitchBotAccountProvider, IHostBotAccountTokenStatusProvider
{
    private static readonly TimeSpan _refreshSkew = TimeSpan.FromMinutes(1);

    public async Task<BotAccountAuthorizationStatus> GetStatusAsync(
        int hostId,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var settings = await db.HostBotAccountSettings.SingleOrDefaultAsync(
            x => x.HostId == hostId,
            ct
        );
        var required = RequiredScopes(settings);

        if (settings is null || !settings.OverrideEnabled)
        {
            return new(
                null,
                settings?.Login,
                settings?.ProfileImageUrl,
                BotAccountAuthorizationState.Disabled,
                required,
                SplitStoredScopes(settings?.AuthorizedScopes).ToArray(),
                [],
                "This channel is using the main BlokeBot account."
            );
        }

        var tokenStatus = await GetStoredTokenStatusAsync(db, settings, required, ct);
        await tokenStatus.Match(
            _ => Task.CompletedTask,
            _ => Task.CompletedTask,
            _ => Task.CompletedTask,
            missingScopes =>
                RefreshProfileMetadataAsync(db, settings, missingScopes.AccessToken, ct),
            ready => RefreshProfileMetadataAsync(db, settings, ready.AccessToken, ct)
        );

        return ToAuthorizationStatus(settings, tokenStatus);
    }

    public async Task<string[]> GetRequiredScopesAsync(int hostId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var settings = await db
            .HostBotAccountSettings.AsNoTracking()
            .SingleOrDefaultAsync(x => x.HostId == hostId, ct);
        return RequiredScopes(settings);
    }

    public async Task<ActiveBotAccountTokenStatus> GetActiveTokenStatusAsync(
        string channelLogin,
        IEnumerable<string?> requiredScopes,
        CancellationToken ct
    )
    {
        var required = ImmutableArray.CreateRange(TwitchScopeSet.NormalizeMany(requiredScopes));
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
                var status = await GetStoredTokenStatusAsync(db, settings, required, ct);
                return ActiveStatus(settings.Login, settings.ProfileImageUrl, status);
            }
        }

        var configuredBotLogin = botSettings.Identity.BotUsername;
        var inspection = await globalTokenStatus
            .GetUserAccessTokenStatus(required)
            .ExecuteAsync(ct);
        var globalStatus = inspection.Match<TwitchTokenStatus>(
            status => status,
            error => new TwitchTokenStatus.Unknown(error)
        );
        return ActiveStatus(configuredBotLogin, null, globalStatus);
    }

    public async Task<ActiveBotAccountTokenStatus> GetCustomBotTokenStatusAsync(
        int hostId,
        IEnumerable<string?> requiredScopes,
        CancellationToken ct
    )
    {
        var required = ImmutableArray.CreateRange(TwitchScopeSet.NormalizeMany(requiredScopes));
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var settings = await db.HostBotAccountSettings.SingleOrDefaultAsync(
            x => x.HostId == hostId,
            ct
        );
        if (settings?.OverrideEnabled != true)
        {
            return new ActiveBotAccountTokenStatus
            {
                BotLogin = string.Empty,
                ProfileImageUrl = settings?.ProfileImageUrl,
                Status = new TwitchTokenStatus.Unavailable(
                    TwitchAccessTokenUnavailableReason.MissingRefreshToken,
                    required
                ),
            };
        }

        var status = await GetStoredTokenStatusAsync(db, settings, required, ct);
        return ActiveStatus(settings.Login, settings.ProfileImageUrl, status);
    }

    public async ValueTask<TwitchBotAccount> GetBotAccountAsync(
        string channelLogin,
        CancellationToken cancellationToken
    )
    {
        var status = await GetActiveTokenStatusAsync(
            channelLogin,
            botSettings.Identity.Scopes,
            cancellationToken
        );
        return status.Status.Match(
            _ => throw BotNotReady(channelLogin),
            unavailable =>
                throw new TwitchAccessTokenUnavailableException(
                    unavailable.Reason,
                    TwitchAccessTokenUnavailableException.MissingRefreshTokenMessage
                ),
            _ => throw BotNotReady(channelLogin),
            _ => throw BotNotReady(channelLogin),
            ready => new TwitchBotAccount(
                TwitchLogin.Normalize(ready.Validation.Login),
                ready.AccessToken
            )
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
        var host = await db.Hosts.SingleOrDefaultAsync(x => x.Id == hostId, ct);
        if (host is null)
        {
            return;
        }

        var settings = await EnsureSettingsAsync(db, hostId, ct);
        if (settings is null)
        {
            return;
        }

        if (settings.OverrideEnabled == enabled)
        {
            return;
        }

        var restartRuntime =
            host.BotRuntimeState
            is BotChannelRuntimeState.Starting
                or BotChannelRuntimeState.Started;
        settings.OverrideEnabled = enabled;
        if (!enabled)
        {
            settings.WhisperResponsesEnabled = false;
        }

        settings.UpdatedAtUtc = DateTime.UtcNow;

        if (restartRuntime)
        {
            host.BotRuntimeState = await CanStartWithSelectedBotAccountAsync(
                db,
                settings,
                enabled,
                ct
            )
                ? BotChannelRuntimeState.Starting
                : BotChannelRuntimeState.Stopped;
            host.BotRuntimeStateChangedAtUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        await changes.NotifyChangedAsync(ct);
    }

    public async Task<bool> SetWhisperResponsesEnabledAsync(
        int hostId,
        bool enabled,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var settings = await EnsureSettingsAsync(db, hostId, ct);
        if (settings is null)
        {
            return false;
        }

        if (enabled && !settings.OverrideEnabled)
        {
            return false;
        }

        if (settings.WhisperResponsesEnabled == enabled)
        {
            return true;
        }

        settings.WhisperResponsesEnabled = enabled;
        settings.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await changes.NotifyChangedAsync(ct);
        return true;
    }

    public async Task<BotAccountAuthorizationResult> AuthorizeAsync(
        int hostId,
        HostBotAccountAuthorizationGrant grant,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var settings = await EnsureSettingsAsync(db, hostId, ct);
        if (settings is null)
        {
            return BotAccountAuthorizationResult.Failure("Channel was not found.");
        }

        if (!settings.OverrideEnabled)
        {
            return BotAccountAuthorizationResult.Failure(
                "Turn on custom bot before connecting it."
            );
        }

        var missingScopes = TwitchScopeSet.Missing(grant.Scopes, RequiredScopes(settings));
        if (missingScopes.Length > 0)
        {
            return BotAccountAuthorizationResult.Failure(
                "The bot account needs more Twitch access.",
                missingScopes
            );
        }

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
        await changes.NotifyChangedAsync(ct);

        return BotAccountAuthorizationResult.Success("The bot account is ready.");
    }

    public async Task ClearAsync(int hostId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var settings = await db.HostBotAccountSettings.SingleOrDefaultAsync(
            x => x.HostId == hostId,
            ct
        );
        if (settings is null)
        {
            return;
        }

        ClearAuthorization(settings);
        settings.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await changes.NotifyChangedAsync(ct);
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
        {
            return settings;
        }

        if (!await db.Hosts.AnyAsync(x => x.Id == hostId, ct))
        {
            return null;
        }

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
        var required = ImmutableArray.CreateRange(TwitchScopeSet.NormalizeMany(requiredScopes));
        if (string.IsNullOrWhiteSpace(settings.RefreshToken))
        {
            return new TwitchTokenStatus.Unavailable(
                TwitchAccessTokenUnavailableReason.MissingRefreshToken,
                required
            );
        }

        var accessToken = settings.AccessToken;
        if (string.IsNullOrWhiteSpace(accessToken) || TokenExpiresSoon(settings))
        {
            if (!await RefreshTokenAsync(db, settings, ct))
            {
                return new TwitchTokenStatus.Invalid(required);
            }

            accessToken = settings.AccessToken;
        }

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return new TwitchTokenStatus.Unavailable(
                TwitchAccessTokenUnavailableReason.MissingRefreshToken,
                required
            );
        }

        var validation = await oauth.ValidateTokenAsync(accessToken, ct);
        if (validation is null)
        {
            if (
                !await RefreshTokenAsync(db, settings, ct)
                || string.IsNullOrWhiteSpace(settings.AccessToken)
            )
            {
                return new TwitchTokenStatus.Invalid(required);
            }

            accessToken = settings.AccessToken;
            validation = await oauth.ValidateTokenAsync(accessToken, ct);
            if (validation is null)
            {
                return new TwitchTokenStatus.Invalid(required);
            }
        }

        var granted = TwitchScopeSet.NormalizeMany(validation.Scopes);
        var missing = TwitchScopeSet.Missing(granted, required);
        settings.AuthorizedScopes = TwitchScopeSet.Format(granted);
        settings.Login = validation.Login;
        settings.TwitchUserId = validation.UserId;
        settings.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        var immutableGranted = ImmutableArray.CreateRange(granted);
        var immutableMissing = ImmutableArray.CreateRange(missing);
        return immutableMissing.IsEmpty
            ? new TwitchTokenStatus.Ready(accessToken, validation, required, immutableGranted)
            : new TwitchTokenStatus.MissingScopes(
                accessToken,
                validation,
                required,
                immutableGranted,
                immutableMissing
            );
    }

    private async Task<bool> RefreshTokenAsync(
        BlokeBotDbContext db,
        HostBotAccountSettings settings,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(settings.RefreshToken))
        {
            return false;
        }

        try
        {
            var refreshed = await oauth.RefreshAsync(
                botSettings.Identity.ClientId,
                botSettings.Identity.ClientSecret,
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
            new TwitchHelixRequestContext(botSettings.Identity.ClientId, accessToken),
            ct
        );
        if (user is null)
        {
            return;
        }

        settings.DisplayName = string.IsNullOrWhiteSpace(user.DisplayName)
            ? settings.DisplayName
            : user.DisplayName;
        settings.ProfileImageUrl = string.IsNullOrWhiteSpace(user.ProfileImageUrl)
            ? settings.ProfileImageUrl
            : user.ProfileImageUrl;
        settings.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private async Task<bool> CanStartWithSelectedBotAccountAsync(
        BlokeBotDbContext db,
        HostBotAccountSettings settings,
        bool overrideEnabled,
        CancellationToken ct
    )
    {
        var required = botSettings.Identity.Scopes;
        if (!overrideEnabled)
        {
            var globalInspection = await globalTokenStatus
                .GetUserAccessTokenStatus(required)
                .ExecuteAsync(ct);
            return globalInspection.Match(IsReady, _ => false);
        }

        var customStatus = await GetStoredTokenStatusAsync(
            db,
            settings,
            RequiredScopes(settings),
            ct
        );
        return IsReady(customStatus);
    }

    private string[] RequiredScopes(HostBotAccountSettings? settings)
    {
        var scopes = hostBotOAuth.RequestedScopes();
        return settings?.WhisperResponsesEnabled == true
            ? TwitchScopeSet.NormalizeMany(scopes.Append(TwitchScopes.UserManageWhispers))
            : scopes;
    }

    private static BotAccountAuthorizationStatus ToAuthorizationStatus(
        HostBotAccountSettings settings,
        TwitchTokenStatus status
    )
    {
        return status.Match<BotAccountAuthorizationStatus>(
            unknown =>
                new(
                    null,
                    settings.Login,
                    settings.ProfileImageUrl,
                    BotAccountAuthorizationState.Unknown,
                    unknown.Error.RequiredScopes,
                    [],
                    unknown.Error.RequiredScopes,
                    "BlokeBot could not check the custom bot account right now."
                ),
            unavailable =>
                new(
                    null,
                    settings.Login,
                    settings.ProfileImageUrl,
                    BotAccountAuthorizationState.NotAuthorized,
                    unavailable.RequiredScopes,
                    [],
                    unavailable.RequiredScopes,
                    "No custom bot account is connected yet."
                ),
            invalid =>
                new(
                    null,
                    settings.Login,
                    settings.ProfileImageUrl,
                    BotAccountAuthorizationState.NotAuthorized,
                    invalid.RequiredScopes,
                    [],
                    invalid.RequiredScopes,
                    "BlokeBot could not check the custom bot account."
                ),
            missingScopes =>
                new(
                    null,
                    missingScopes.Validation.Login,
                    settings.ProfileImageUrl,
                    BotAccountAuthorizationState.MissingScopes,
                    missingScopes.RequiredScopes,
                    missingScopes.GrantedScopes,
                    missingScopes.Missing,
                    "The custom bot account needs more Twitch access."
                ),
            ready =>
                new(
                    null,
                    ready.Validation.Login,
                    settings.ProfileImageUrl,
                    BotAccountAuthorizationState.Ready,
                    ready.RequiredScopes,
                    ready.GrantedScopes,
                    [],
                    "The custom bot account is ready."
                )
        );
    }

    private static bool TokenExpiresSoon(HostBotAccountSettings settings)
    {
        return settings.ExpiresAtUtc is null
            || settings.ExpiresAtUtc <= DateTimeOffset.UtcNow.Add(_refreshSkew);
    }

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

    private static IEnumerable<string> SplitStoredScopes(string? scopes)
    {
        return (scopes ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    private static ActiveBotAccountTokenStatus ActiveStatus(
        string? configuredLogin,
        string? profileImageUrl,
        TwitchTokenStatus status
    )
    {
        var botLogin = status.Match(
            _ => configuredLogin,
            _ => configuredLogin,
            _ => configuredLogin,
            missingScopes => missingScopes.Validation.Login,
            ready => ready.Validation.Login
        );
        return new ActiveBotAccountTokenStatus
        {
            BotLogin = botLogin ?? string.Empty,
            ProfileImageUrl = profileImageUrl,
            Status = status,
        };
    }

    private static bool IsReady(TwitchTokenStatus status)
    {
        return status.Match(_ => false, _ => false, _ => false, _ => false, _ => true);
    }

    private static InvalidOperationException BotNotReady(string channelLogin)
    {
        return new($"The bot for #{channelLogin} is not ready yet.");
    }
}
