using System.Collections.Immutable;
using System.Diagnostics;
using BlokeBot.Features.HostedChannels.Runtime;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Features.HostedChannels.Authorization;

public sealed class HostBotAccountAuthorizationService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    HostBotAccountOAuthService hostBotOAuth,
    OAuthTransport transport,
    HelixClient helix,
    ITokenStatusSource globalTokenStatus,
    HostedChannelChangeNotifier changes,
    BotSettings botSettings
) : IBotAccountProvider, IHostBotAccountTokenStatusProvider
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
        var required = ImmutableArray.CreateRange(ScopeSet.NormalizeMany(requiredScopes));
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var host = await db
            .Hosts.AsNoTracking()
            .Where(x => x.Login == Login.Normalize(channelLogin))
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
        var globalStatus = inspection.Match<TokenStatus>(
            status => status,
            error => new TokenStatus.Unknown(error)
        );
        return ActiveStatus(configuredBotLogin, null, globalStatus);
    }

    public async Task<ActiveBotAccountTokenStatus> GetCustomBotTokenStatusAsync(
        int hostId,
        IEnumerable<string?> requiredScopes,
        CancellationToken ct
    )
    {
        var required = ImmutableArray.CreateRange(ScopeSet.NormalizeMany(requiredScopes));
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
                Status = new TokenStatus.Unavailable(
                    AccessTokenUnavailableReason.MissingRefreshToken,
                    required
                ),
            };
        }

        var status = await GetStoredTokenStatusAsync(db, settings, required, ct);
        return ActiveStatus(settings.Login, settings.ProfileImageUrl, status);
    }

    public async ValueTask<BotAccount> GetBotAccountAsync(
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
                throw new AccessTokenUnavailableException(
                    unavailable.Reason,
                    AccessTokenUnavailableException.MissingRefreshTokenMessage
                ),
            _ => throw BotNotReady(channelLogin),
            _ => throw BotNotReady(channelLogin),
            ready => new BotAccount(Login.Normalize(ready.Validation.Login), ready.AccessToken)
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

    public Task UseCustomBotAsync(int hostId, CancellationToken ct)
    {
        return SelectBotAccountAsync(hostId, BotAccountSelection.Custom, ct);
    }

    public Task UseMainBotAsync(int hostId, CancellationToken ct)
    {
        return SelectBotAccountAsync(hostId, BotAccountSelection.Main, ct);
    }

    public Task<WhisperResponseConfigurationOutcome> EnableWhisperResponsesAsync(
        int hostId,
        CancellationToken ct
    )
    {
        return ConfigureWhisperResponsesAsync(hostId, WhisperResponseConfiguration.Enabled, ct);
    }

    public Task<WhisperResponseConfigurationOutcome> DisableWhisperResponsesAsync(
        int hostId,
        CancellationToken ct
    )
    {
        return ConfigureWhisperResponsesAsync(hostId, WhisperResponseConfiguration.Disabled, ct);
    }

    private async Task SelectBotAccountAsync(
        int hostId,
        BotAccountSelection selection,
        CancellationToken ct
    )
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

        var overrideEnabled = selection switch
        {
            BotAccountSelection.Main => false,
            BotAccountSelection.Custom => true,
            _ => throw new UnreachableException("Unknown bot account selection."),
        };
        if (settings.OverrideEnabled == overrideEnabled)
        {
            return;
        }

        var runtimeLifecycle = HostedChannelRuntimeLifecycle.FromPersistence(
            host.BotRuntimeState,
            host.BotRuntimeStateChangedAtUtc
        );
        var restartRuntime =
            runtimeLifecycle
            is HostedChannelRuntimeLifecycle.Starting
                or HostedChannelRuntimeLifecycle.Started;
        settings.OverrideEnabled = overrideEnabled;
        if (selection is BotAccountSelection.Main)
        {
            settings.WhisperResponsesEnabled = false;
        }

        settings.UpdatedAtUtc = DateTime.UtcNow;

        if (restartRuntime)
        {
            host.BotRuntimeState = await CanStartWithSelectedBotAccountAsync(
                db,
                settings,
                selection,
                ct
            )
                ? BotChannelRuntimeState.Starting
                : BotChannelRuntimeState.Stopped;
            host.BotRuntimeStateChangedAtUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        await changes.NotifyChangedAsync(ct);
    }

    private async Task<WhisperResponseConfigurationOutcome> ConfigureWhisperResponsesAsync(
        int hostId,
        WhisperResponseConfiguration configuration,
        CancellationToken ct
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var settings = await EnsureSettingsAsync(db, hostId, ct);
        if (settings is null)
        {
            return new WhisperResponseConfigurationOutcome.HostNotFound();
        }

        if (configuration is WhisperResponseConfiguration.Enabled && !settings.OverrideEnabled)
        {
            return new WhisperResponseConfigurationOutcome.CustomBotRequired();
        }

        var enabled = configuration switch
        {
            WhisperResponseConfiguration.Enabled => true,
            WhisperResponseConfiguration.Disabled => false,
            _ => throw new UnreachableException("Unknown whisper response configuration."),
        };
        if (settings.WhisperResponsesEnabled == enabled)
        {
            return new WhisperResponseConfigurationOutcome.Configured();
        }

        settings.WhisperResponsesEnabled = enabled;
        settings.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await changes.NotifyChangedAsync(ct);
        return new WhisperResponseConfigurationOutcome.Configured();
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

        var missingScopes = ScopeSet.Missing(grant.Scopes, RequiredScopes(settings));
        if (missingScopes.Length > 0)
        {
            return BotAccountAuthorizationResult.Failure(
                "The bot account needs more Twitch access.",
                missingScopes
            );
        }

        settings.AccessToken = grant.Token.AccessToken;
        settings.AuthorizedAtUtc = DateTime.UtcNow;
        settings.AuthorizedScopes = ScopeSet.Format(grant.Scopes);
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

    private async Task<TokenStatus> GetStoredTokenStatusAsync(
        BlokeBotDbContext db,
        HostBotAccountSettings settings,
        IEnumerable<string?> requiredScopes,
        CancellationToken ct
    )
    {
        var required = ImmutableArray.CreateRange(ScopeSet.NormalizeMany(requiredScopes));
        if (string.IsNullOrWhiteSpace(settings.RefreshToken))
        {
            return new TokenStatus.Unavailable(
                AccessTokenUnavailableReason.MissingRefreshToken,
                required
            );
        }

        var accessToken = settings.AccessToken;
        if (string.IsNullOrWhiteSpace(accessToken) || TokenExpiresSoon(settings))
        {
            if (!await RefreshTokenAsync(db, settings, ct))
            {
                return new TokenStatus.Invalid(required);
            }

            accessToken = settings.AccessToken;
        }

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return new TokenStatus.Unavailable(
                AccessTokenUnavailableReason.MissingRefreshToken,
                required
            );
        }

        var validation = await transport.ValidateTokenAsync(accessToken, ct);
        if (validation.Match(static _ => false, static _ => true))
        {
            if (
                !await RefreshTokenAsync(db, settings, ct)
                || string.IsNullOrWhiteSpace(settings.AccessToken)
            )
            {
                return new TokenStatus.Invalid(required);
            }

            accessToken = settings.AccessToken;
            validation = await transport.ValidateTokenAsync(accessToken, ct);
        }

        return await validation.Match(
            validated =>
                PersistValidatedStatusAsync(
                    db,
                    settings,
                    accessToken,
                    validated.Validation,
                    required,
                    ct
                ),
            _ => Task.FromResult<TokenStatus>(new TokenStatus.Invalid(required))
        );
    }

    private static async Task<TokenStatus> PersistValidatedStatusAsync(
        BlokeBotDbContext db,
        HostBotAccountSettings settings,
        string accessToken,
        TokenValidation validation,
        ImmutableArray<string> required,
        CancellationToken ct
    )
    {
        var granted = ScopeSet.NormalizeMany(validation.Scopes);
        var missing = ScopeSet.Missing(granted, required);
        settings.AuthorizedScopes = ScopeSet.Format(granted);
        settings.Login = validation.Login;
        settings.TwitchUserId = validation.UserId;
        settings.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        var immutableGranted = ImmutableArray.CreateRange(granted);
        var immutableMissing = ImmutableArray.CreateRange(missing);
        return immutableMissing.IsEmpty
            ? new TokenStatus.Ready(accessToken, validation, required, immutableGranted)
            : new TokenStatus.MissingScopes(
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
            var refreshed = await transport.RefreshAsync(
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
            new HelixRequestContext(botSettings.Identity.ClientId, accessToken),
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
        BotAccountSelection selection,
        CancellationToken ct
    )
    {
        var required = botSettings.Identity.Scopes;
        if (selection is BotAccountSelection.Main)
        {
            var globalInspection = await globalTokenStatus
                .GetUserAccessTokenStatus(required)
                .ExecuteAsync(ct);
            return globalInspection.Match(IsReady, _ => false);
        }

        if (selection is BotAccountSelection.Custom)
        {
            var customStatus = await GetStoredTokenStatusAsync(
                db,
                settings,
                RequiredScopes(settings),
                ct
            );
            return IsReady(customStatus);
        }

        throw new UnreachableException("Unknown bot account selection.");
    }

    private string[] RequiredScopes(HostBotAccountSettings? settings)
    {
        var scopes = hostBotOAuth.RequestedScopes();
        return settings?.WhisperResponsesEnabled == true
            ? ScopeSet.NormalizeMany(scopes.Append(Scopes.UserManageWhispers))
            : scopes.ToArray();
    }

    private static BotAccountAuthorizationStatus ToAuthorizationStatus(
        HostBotAccountSettings settings,
        TokenStatus status
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
        TokenStatus status
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

    private static bool IsReady(TokenStatus status)
    {
        return status.Match(_ => false, _ => false, _ => false, _ => false, _ => true);
    }

    private static InvalidOperationException BotNotReady(string channelLogin)
    {
        return new($"The bot for #{channelLogin} is not ready yet.");
    }

    private enum BotAccountSelection
    {
        Main,
        Custom,
    }

    private enum WhisperResponseConfiguration
    {
        Disabled,
        Enabled,
    }
}
