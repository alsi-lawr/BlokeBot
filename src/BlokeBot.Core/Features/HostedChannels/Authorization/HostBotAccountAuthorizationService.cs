using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Functional;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.HostedChannels.Authorization;

public sealed class HostBotAccountAuthorizationService(
    IDbContextFactory<BlokeBotDbContext> dbFactory,
    HostBotAccountOAuthService hostBotOAuth,
    OAuthTransport transport,
    HelixClient helix,
    IHostBotAccountTokenProtector tokenProtector,
    ITokenStatusSource globalTokenStatus,
    HostedChannelChangeNotifier changes,
    BotSettings botSettings,
    HostedChannelRuntimeTransitionService runtimeTransitions
) : IBotAccountProvider, IHostBotAccountTokenStatusProvider
{
    private static readonly TimeSpan _refreshSkew = TimeSpan.FromMinutes(1);
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _credentialMutationGates = new();

    public async Task<BotAccountAuthorizationStatus> GetStatusAsync(
        int hostId,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var settings = await db.HostBotAccountSettings.SingleOrDefaultAsync(
            x => x.HostId == hostId,
            cancellationToken
        );
        var required = RequiredScopes(
            settings,
            await PinEnabledAsync(db, hostId, cancellationToken)
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
                [],
                "This channel is using the main BlokeBot account."
            );
        }

        var tokenStatus = await GetStoredTokenStatusAsync(
            db,
            settings,
            required,
            cancellationToken
        );
        await tokenStatus.Match(
            _ => Task.CompletedTask,
            _ => Task.CompletedTask,
            _ => Task.CompletedTask,
            missingScopes =>
                RefreshProfileMetadataAsync(
                    db,
                    settings,
                    missingScopes.AccessToken,
                    cancellationToken
                ),
            ready => RefreshProfileMetadataAsync(db, settings, ready.AccessToken, cancellationToken)
        );

        return ToAuthorizationStatus(settings, tokenStatus);
    }

    public async Task<string[]> GetRequiredScopesAsync(
        int hostId,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var settings = await db
            .HostBotAccountSettings.AsNoTracking()
            .SingleOrDefaultAsync(x => x.HostId == hostId, cancellationToken);
        return RequiredScopes(settings, await PinEnabledAsync(db, hostId, cancellationToken));
    }

    public async Task<ActiveBotAccountTokenStatus> GetActiveTokenStatusAsync(
        string channelLogin,
        IEnumerable<string?> requiredScopes,
        CancellationToken cancellationToken
    )
    {
        var required = ImmutableArray.CreateRange(ScopeSet.NormalizeMany(requiredScopes));
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var host = await db
            .Hosts.AsNoTracking()
            .Where(x => x.Login == Login.Normalize(channelLogin))
            .Select(x => new { x.Id })
            .SingleOrDefaultAsync(cancellationToken);

        if (host is not null)
        {
            var settings = await db.HostBotAccountSettings.SingleOrDefaultAsync(
                x => x.HostId == host.Id,
                cancellationToken
            );
            if (settings?.OverrideEnabled == true)
            {
                var status = await GetStoredTokenStatusAsync(
                    db,
                    settings,
                    required,
                    cancellationToken
                );
                return ActiveStatus(settings.Login, settings.ProfileImageUrl, status);
            }
        }

        var configuredBotLogin = botSettings.Identity.BotUsername;
        var inspection = await globalTokenStatus
            .GetUserAccessTokenStatus(required)
            .ExecuteAsync(cancellationToken);
        var globalStatus = inspection.Match<TokenStatus>(
            status => status,
            error => new TokenStatus.Unknown(error)
        );
        return ActiveStatus(configuredBotLogin, null, globalStatus);
    }

    public async Task<ActiveBotAccountTokenStatus> GetCustomBotTokenStatusAsync(
        int hostId,
        IEnumerable<string?> requiredScopes,
        CancellationToken cancellationToken
    )
    {
        var required = ImmutableArray.CreateRange(ScopeSet.NormalizeMany(requiredScopes));
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var settings = await db.HostBotAccountSettings.SingleOrDefaultAsync(
            x => x.HostId == hostId,
            cancellationToken
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

        var status = await GetStoredTokenStatusAsync(db, settings, required, cancellationToken);
        return ActiveStatus(settings.Login, settings.ProfileImageUrl, status);
    }

    public IO<BotAccount, AccessTokenUnavailableReason> GetBotAccount(string channelLogin) =>
        IO<BotAccount, AccessTokenUnavailableReason>.Create(async cancellationToken =>
        {
            var status = await GetActiveTokenStatusAsync(
                channelLogin,
                botSettings.Identity.Scopes,
                cancellationToken
            );
            return status.Status.Match(
                _ => throw BotNotReady(channelLogin),
                unavailable =>
                    Result<BotAccount, AccessTokenUnavailableReason>.Error(unavailable.Reason),
                _ => throw BotNotReady(channelLogin),
                _ => throw BotNotReady(channelLogin),
                ready =>
                    Result<BotAccount, AccessTokenUnavailableReason>.Success(
                        new BotAccount(Login.Normalize(ready.Validation.Login), ready.AccessToken)
                    )
            );
        });

    public async Task<bool> CanAuthorizeAsync(
        int hostId,
        HostBotAccountActor actor,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var host = await db
            .Hosts.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == hostId, cancellationToken);
        if (host is null || !HasAuthorizationAuthority(host, actor))
        {
            return false;
        }

        var settings = await db.HostBotAccountSettings.SingleOrDefaultAsync(
            x => x.HostId == hostId,
            cancellationToken
        );
        return settings?.OverrideEnabled == true;
    }

    public Task UseCustomBotAsync(int hostId, CancellationToken cancellationToken) =>
        SelectBotAccountAsync(hostId, BotAccountSelection.Custom, cancellationToken);

    public Task UseMainBotAsync(int hostId, CancellationToken cancellationToken) =>
        SelectBotAccountAsync(hostId, BotAccountSelection.Main, cancellationToken);

    public Task<WhisperResponseConfigurationOutcome> EnableWhisperResponsesAsync(
        int hostId,
        CancellationToken cancellationToken
    ) =>
        ConfigureWhisperResponsesAsync(
            hostId,
            WhisperResponseConfiguration.Enabled,
            cancellationToken
        );

    public Task<WhisperResponseConfigurationOutcome> DisableWhisperResponsesAsync(
        int hostId,
        CancellationToken cancellationToken
    ) =>
        ConfigureWhisperResponsesAsync(
            hostId,
            WhisperResponseConfiguration.Disabled,
            cancellationToken
        );

    private async Task SelectBotAccountAsync(
        int hostId,
        BotAccountSelection selection,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var host = await db.Hosts.SingleOrDefaultAsync(x => x.Id == hostId, cancellationToken);
        if (host is null)
        {
            return;
        }

        var settings = await EnsureSettingsAsync(db, hostId, cancellationToken);
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

        var canRestart =
            restartRuntime
            && await CanStartWithSelectedBotAccountAsync(
                db,
                settings,
                selection,
                cancellationToken
            );

        var runtimeChange = restartRuntime
            ? canRestart
                ? HostedChannelAccountSelectionRuntimeChange.Restart
                : HostedChannelAccountSelectionRuntimeChange.Stop
            : HostedChannelAccountSelectionRuntimeChange.None;
        await runtimeTransitions.CommitAccountSelectionAsync(
            db,
            host.Id,
            runtimeChange,
            cancellationToken
        );
        _ = await changes.NotifyChangedAsync(cancellationToken);
    }

    private async Task<WhisperResponseConfigurationOutcome> ConfigureWhisperResponsesAsync(
        int hostId,
        WhisperResponseConfiguration configuration,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var settings = await EnsureSettingsAsync(db, hostId, cancellationToken);
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
        _ = await db.SaveChangesAsync(cancellationToken);
        _ = await changes.NotifyChangedAsync(cancellationToken);
        return new WhisperResponseConfigurationOutcome.Configured();
    }

    public IO<HostBotAccountAuthorizationOutcome, Never> Authorize(
        int hostId,
        HostBotAccountActor actor,
        HostBotAccountAuthorizationGrant grant
    ) =>
        IO<HostBotAccountAuthorizationOutcome, Never>.Create(async cancellationToken =>
        {
            var mutationGate = CredentialMutationGate(hostId);
            HostBotAccountAuthorizationOutcome.Authorized? committed = null;
            await mutationGate.WaitAsync(cancellationToken);
            try
            {
                await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
                var host = await db
                    .Hosts.AsNoTracking()
                    .SingleOrDefaultAsync(x => x.Id == hostId, cancellationToken);
                if (host is null)
                {
                    return Success(new HostBotAccountAuthorizationOutcome.HostNotFound());
                }

                if (!HasAuthorizationAuthority(host, actor))
                {
                    return Success(new HostBotAccountAuthorizationOutcome.AuthorityDenied());
                }

                var settings = await EnsureSettingsAsync(db, hostId, cancellationToken);
                Debug.Assert(settings is not null, "The authorized host must have settings.");

                if (!settings.OverrideEnabled)
                {
                    return Success(new HostBotAccountAuthorizationOutcome.OverrideDisabled());
                }

                var missingScopes = ScopeSet.Missing(grant.Scopes, RequiredScopes(settings));
                if (missingScopes.Length > 0)
                {
                    return Success(
                        new HostBotAccountAuthorizationOutcome.MissingScopes(missingScopes)
                    );
                }

                var protectedToken = tokenProtector.Protect(hostId, grant.Token);
                var outcome = await protectedToken.Match<Task<HostBotAccountAuthorizationOutcome>>(
                    async protectedPayload =>
                    {
                        settings.ProtectedTokenPayload = protectedPayload;
                        settings.AuthorizedAtUtc = DateTime.UtcNow;
                        settings.AuthorizedScopes = ScopeSet.Format(grant.Scopes);
                        settings.DisplayName = grant.DisplayName.Trim();
                        settings.Login = grant.Login.Value;
                        settings.ProfileImageUrl = string.IsNullOrWhiteSpace(grant.ProfileImageUrl)
                            ? null
                            : grant.ProfileImageUrl.Trim();
                        settings.TwitchUserId = grant.UserId;
                        settings.UpdatedAtUtc = DateTime.UtcNow;
                        _ = await db.SaveChangesAsync(cancellationToken);

                        return new HostBotAccountAuthorizationOutcome.Authorized();
                    },
                    failure =>
                        Task.FromResult<HostBotAccountAuthorizationOutcome>(
                            new HostBotAccountAuthorizationOutcome.ProtectionUnavailable(failure)
                        )
                );
                if (outcome is not HostBotAccountAuthorizationOutcome.Authorized authorized)
                {
                    return Success(outcome);
                }

                committed = authorized;
            }
            finally
            {
                _ = mutationGate.Release();
            }

            Debug.Assert(committed is not null, "The custom-bot grant must be committed.");
            _ = await changes.NotifyChangedAsync(cancellationToken);
            return Success(committed);
        });

    public async Task<HostBotAccountClearOutcome> ClearAsync(
        int hostId,
        HostBotAccountActor actor,
        CancellationToken cancellationToken
    )
    {
        var mutationGate = CredentialMutationGate(hostId);
        var committed = false;
        await mutationGate.WaitAsync(cancellationToken);
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var host = await db
                .Hosts.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == hostId, cancellationToken);
            if (host is null)
            {
                return new HostBotAccountClearOutcome.HostNotFound();
            }

            if (!HasAuthorizationAuthority(host, actor))
            {
                return new HostBotAccountClearOutcome.AuthorityDenied();
            }

            var settings = await db.HostBotAccountSettings.SingleOrDefaultAsync(
                x => x.HostId == hostId,
                cancellationToken
            );
            if (settings is null)
            {
                return new HostBotAccountClearOutcome.Cleared();
            }

            ClearAuthorization(settings);
            settings.UpdatedAtUtc = DateTime.UtcNow;
            _ = await db.SaveChangesAsync(cancellationToken);
            committed = true;
        }
        finally
        {
            _ = mutationGate.Release();
        }

        Debug.Assert(committed, "The custom-bot grant clear must be committed.");
        _ = await changes.NotifyChangedAsync(cancellationToken);
        return new HostBotAccountClearOutcome.Cleared();
    }

    private static bool HasAuthorizationAuthority(BotHost host, HostBotAccountActor actor) =>
        actor switch
        {
            HostBotAccountActor.BotAdministrator administrator => !string.IsNullOrWhiteSpace(
                administrator.AuthenticatedUserId
            ) && !string.IsNullOrWhiteSpace(administrator.Login),
            HostBotAccountActor.ChannelOwner owner => !string.IsNullOrWhiteSpace(
                owner.AuthenticatedUserId
            )
                && !string.IsNullOrWhiteSpace(owner.Login)
                && string.Equals(host.Login, Login.Normalize(owner.Login), StringComparison.Ordinal)
                && (
                    string.IsNullOrWhiteSpace(host.TwitchUserId)
                    || string.Equals(
                        host.TwitchUserId,
                        owner.AuthenticatedUserId,
                        StringComparison.Ordinal
                    )
                ),
            _ => throw new UnreachableException("Unknown custom-bot account actor."),
        };

    private async Task<HostBotAccountSettings?> EnsureSettingsAsync(
        BlokeBotDbContext db,
        int hostId,
        CancellationToken cancellationToken
    )
    {
        var settings = await db.HostBotAccountSettings.SingleOrDefaultAsync(
            x => x.HostId == hostId,
            cancellationToken
        );
        if (settings is not null)
        {
            return settings;
        }

        if (!await db.Hosts.AnyAsync(x => x.Id == hostId, cancellationToken))
        {
            return null;
        }

        settings = new HostBotAccountSettings { HostId = hostId, UpdatedAtUtc = DateTime.UtcNow };
        _ = db.HostBotAccountSettings.Add(settings);
        return settings;
    }

    private static Result<HostBotAccountAuthorizationOutcome, Never> Success(
        HostBotAccountAuthorizationOutcome outcome
    ) => Result<HostBotAccountAuthorizationOutcome, Never>.Success(outcome);

    private async Task<TokenStatus> GetStoredTokenStatusAsync(
        BlokeBotDbContext db,
        HostBotAccountSettings settings,
        IEnumerable<string?> requiredScopes,
        CancellationToken cancellationToken
    )
    {
        var required = ImmutableArray.CreateRange(ScopeSet.NormalizeMany(requiredScopes));
        var protectedPayload = settings.ProtectedTokenPayload?.ToArray();
        if (protectedPayload is null)
        {
            return new TokenStatus.Unavailable(
                AccessTokenUnavailableReason.MissingRefreshToken,
                required
            );
        }

        var unprotectedToken = tokenProtector.Unprotect(settings.HostId, protectedPayload);
        return await unprotectedToken.Match(
            payload =>
                GetPlaintextTokenStatusAsync(db, settings, payload, required, cancellationToken),
            async _ =>
            {
                var disabled = await DisableUnusableCredentialsIfCurrentAsync(
                    db,
                    settings,
                    protectedPayload,
                    cancellationToken
                );
                return disabled
                    ? ProtectionUnavailable(required)
                    : await GetStoredTokenStatusAsync(db, settings, required, cancellationToken);
            }
        );
    }

    private async Task<TokenStatus> GetPlaintextTokenStatusAsync(
        BlokeBotDbContext db,
        HostBotAccountSettings settings,
        HostBotAccountTokenPayload payload,
        ImmutableArray<string> required,
        CancellationToken cancellationToken
    )
    {
        if (TokenExpiresSoon(payload))
        {
            var refresh = await RefreshTokenAsync(db, settings, payload, cancellationToken);
            var refreshedPayload = refresh.Match<HostBotAccountTokenPayload?>(
                refreshed => refreshed.Payload,
                _ => null,
                _ => null
            );
            if (refreshedPayload is null)
            {
                return refresh.Match<TokenStatus>(
                    _ => throw new UnreachableException(),
                    _ => new TokenStatus.Invalid(required),
                    _ => ProtectionUnavailable(required)
                );
            }

            payload = refreshedPayload;
        }

        var validation = await transport.ValidateTokenAsync(payload.AccessToken, cancellationToken);
        if (validation.Match(static _ => false, static _ => true))
        {
            var refresh = await RefreshTokenAsync(db, settings, payload, cancellationToken);
            var refreshedPayload = refresh.Match<HostBotAccountTokenPayload?>(
                refreshed => refreshed.Payload,
                _ => null,
                _ => null
            );
            if (refreshedPayload is null)
            {
                return refresh.Match<TokenStatus>(
                    _ => throw new UnreachableException(),
                    _ => new TokenStatus.Invalid(required),
                    _ => ProtectionUnavailable(required)
                );
            }

            payload = refreshedPayload;
            validation = await transport.ValidateTokenAsync(payload.AccessToken, cancellationToken);
        }

        return await validation.Match(
            validated =>
                PersistValidatedStatusAsync(
                    db,
                    settings,
                    payload.AccessToken,
                    validated.Validation,
                    required,
                    cancellationToken
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
        CancellationToken cancellationToken
    )
    {
        var granted = ScopeSet.NormalizeMany(validation.Scopes);
        var missing = ScopeSet.Missing(granted, required);
        settings.AuthorizedScopes = ScopeSet.Format(granted);
        settings.Login = validation.Login;
        settings.TwitchUserId = validation.UserId;
        settings.UpdatedAtUtc = DateTime.UtcNow;
        _ = await db.SaveChangesAsync(cancellationToken);

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

    private async Task<HostBotAccountTokenRefreshOutcome> RefreshTokenAsync(
        BlokeBotDbContext db,
        HostBotAccountSettings settings,
        HostBotAccountTokenPayload current,
        CancellationToken cancellationToken
    )
    {
        var originalProtectedPayload = settings.ProtectedTokenPayload?.ToArray();
        var mutationGate = CredentialMutationGate(settings.HostId);
        await mutationGate.WaitAsync(cancellationToken);
        var mutationGateHeld = true;
        try
        {
            await db.Entry(settings).ReloadAsync(cancellationToken);
            var persistedProtectedPayload = settings.ProtectedTokenPayload;
            if (persistedProtectedPayload is null)
            {
                return new HostBotAccountTokenRefreshOutcome.Rejected();
            }

            if (originalProtectedPayload is null)
            {
                return new HostBotAccountTokenRefreshOutcome.Rejected();
            }

            if (!ProtectedPayloadEquals(originalProtectedPayload, persistedProtectedPayload))
            {
                var latest = tokenProtector.Unprotect(settings.HostId, persistedProtectedPayload);
                return await latest.Match<Task<HostBotAccountTokenRefreshOutcome>>(
                    payload =>
                        Task.FromResult<HostBotAccountTokenRefreshOutcome>(
                            new HostBotAccountTokenRefreshOutcome.Refreshed(payload)
                        ),
                    async failure =>
                    {
                        await DisableUnusableCredentialsAsync(db, settings, cancellationToken);
                        _ = mutationGate.Release();
                        mutationGateHeld = false;
                        _ = await changes.NotifyChangedAsync(cancellationToken);
                        return new HostBotAccountTokenRefreshOutcome.ProtectionUnavailable(failure);
                    }
                );
            }

            var refreshed = await transport.RefreshCompleteTokenSetAsync(
                botSettings.Identity.ClientId,
                botSettings.Identity.ClientSecret,
                current.RefreshToken,
                cancellationToken
            );
            if (string.IsNullOrWhiteSpace(refreshed.RefreshToken))
            {
                return new HostBotAccountTokenRefreshOutcome.Rejected();
            }

            var refreshedPayload = new HostBotAccountTokenPayload(
                refreshed.AccessToken,
                refreshed.RefreshToken,
                DateTimeOffset.UtcNow.AddSeconds(refreshed.ExpiresIn)
            );
            var validation = await transport.ValidateTokenAsync(
                refreshed.AccessToken,
                cancellationToken
            );
            if (
                validation is not TokenValidationOutcome.Validated validated
                || !RefreshedIdentityMatches(settings, validated.Validation)
                || ScopeSet.Missing(validated.Validation.Scopes, RequiredScopes(settings)).Length
                    > 0
            )
            {
                return new HostBotAccountTokenRefreshOutcome.Rejected();
            }

            var protectedToken = tokenProtector.Protect(settings.HostId, refreshedPayload);
            return await protectedToken.Match<Task<HostBotAccountTokenRefreshOutcome>>(
                async protectedPayload =>
                {
                    settings.ProtectedTokenPayload = protectedPayload;
                    settings.UpdatedAtUtc = DateTime.UtcNow;
                    _ = await db.SaveChangesAsync(cancellationToken);
                    return new HostBotAccountTokenRefreshOutcome.Refreshed(refreshedPayload);
                },
                failure =>
                    Task.FromResult<HostBotAccountTokenRefreshOutcome>(
                        new HostBotAccountTokenRefreshOutcome.ProtectionUnavailable(failure)
                    )
            );
        }
        catch (HttpRequestException exception)
            when (exception.StatusCode
                    is System.Net.HttpStatusCode.BadRequest
                        or System.Net.HttpStatusCode.Unauthorized
            )
        {
            return new HostBotAccountTokenRefreshOutcome.Rejected();
        }
        finally
        {
            if (mutationGateHeld)
            {
                _ = mutationGate.Release();
            }
        }
    }

    private SemaphoreSlim CredentialMutationGate(int hostId) =>
        _credentialMutationGates.GetOrAdd(hostId, static _ => new SemaphoreSlim(1, 1));

    private static bool RefreshedIdentityMatches(
        HostBotAccountSettings settings,
        TokenValidation validation
    ) =>
        (
            string.IsNullOrWhiteSpace(settings.TwitchUserId)
            || string.Equals(settings.TwitchUserId, validation.UserId, StringComparison.Ordinal)
        )
        && (
            string.IsNullOrWhiteSpace(settings.Login)
            || string.Equals(settings.Login, validation.Login, StringComparison.Ordinal)
        );

    private static bool ProtectedPayloadEquals(byte[] left, byte[] right) =>
        left.AsSpan().SequenceEqual(right);

    private async Task<bool> DisableUnusableCredentialsIfCurrentAsync(
        BlokeBotDbContext db,
        HostBotAccountSettings settings,
        byte[] failedProtectedPayload,
        CancellationToken cancellationToken
    )
    {
        var mutationGate = CredentialMutationGate(settings.HostId);
        var disabled = false;
        await mutationGate.WaitAsync(cancellationToken);
        try
        {
            await db.Entry(settings).ReloadAsync(cancellationToken);
            if (
                settings.ProtectedTokenPayload is null
                || !ProtectedPayloadEquals(failedProtectedPayload, settings.ProtectedTokenPayload)
            )
            {
                return false;
            }

            await DisableUnusableCredentialsAsync(db, settings, cancellationToken);
            disabled = true;
        }
        finally
        {
            _ = mutationGate.Release();
        }

        Debug.Assert(disabled, "The unusable custom-bot credentials must be disabled.");
        _ = await changes.NotifyChangedAsync(cancellationToken);
        return true;
    }

    private async Task DisableUnusableCredentialsAsync(
        BlokeBotDbContext db,
        HostBotAccountSettings settings,
        CancellationToken cancellationToken
    )
    {
        var now = DateTime.UtcNow;
        settings.OverrideEnabled = false;
        settings.WhisperResponsesEnabled = false;
        ClearAuthorization(settings);
        settings.UpdatedAtUtc = now;

        var alertExists = await db.DurableAlerts.AnyAsync(
            value =>
                value.HostId == settings.HostId
                && value.Source == CustomBotCredentialAlert.Source
                && value.SourceKey == CustomBotCredentialAlert.SourceKey
                && value.AcknowledgedAtUtc == null,
            cancellationToken
        );
        if (!alertExists)
        {
            _ = db.DurableAlerts.Add(
                new DurableAlert
                {
                    HostId = settings.HostId,
                    Severity = DurableAlertSeverity.Warning,
                    Source = CustomBotCredentialAlert.Source,
                    SourceKey = CustomBotCredentialAlert.SourceKey,
                    Title = CustomBotCredentialAlert.Title,
                    Message = CustomBotCredentialAlert.Message,
                    LinkPath = CustomBotCredentialAlert.LinkPath,
                    CreatedAtUtc = now,
                }
            );
        }

        await runtimeTransitions.CommitCredentialPolicyStopAsync(
            db,
            settings.HostId,
            cancellationToken
        );
    }

    private async Task RefreshProfileMetadataAsync(
        BlokeBotDbContext db,
        HostBotAccountSettings settings,
        string accessToken,
        CancellationToken cancellationToken
    )
    {
        var user = await helix.GetCurrentUserAsync(
            new HelixRequestContext(botSettings.Identity.ClientId, accessToken),
            cancellationToken
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
        _ = await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<bool> CanStartWithSelectedBotAccountAsync(
        BlokeBotDbContext db,
        HostBotAccountSettings settings,
        BotAccountSelection selection,
        CancellationToken cancellationToken
    )
    {
        var required = botSettings.Identity.Scopes;
        if (selection is BotAccountSelection.Main)
        {
            var globalInspection = await globalTokenStatus
                .GetUserAccessTokenStatus(required)
                .ExecuteAsync(cancellationToken);
            return globalInspection.Match(IsReady, _ => false);
        }

        if (selection is BotAccountSelection.Custom)
        {
            var customStatus = await GetStoredTokenStatusAsync(
                db,
                settings,
                RequiredScopes(settings),
                cancellationToken
            );
            return IsReady(customStatus);
        }

        throw new UnreachableException("Unknown bot account selection.");
    }

    private string[] RequiredScopes(HostBotAccountSettings? settings, bool pinEnabled = false)
    {
        IEnumerable<string?> scopes = hostBotOAuth.RequestedScopes();
        if (settings?.WhisperResponsesEnabled == true)
        {
            scopes = scopes.Append(Scopes.UserManageWhispers);
        }

        if (pinEnabled)
        {
            scopes = scopes.Append(Scopes.ModeratorManageChatMessages);
        }

        return ScopeSet.NormalizeMany(scopes);
    }

    private static Task<bool> PinEnabledAsync(
        BlokeBotDbContext db,
        int hostId,
        CancellationToken cancellationToken
    ) =>
        db
            .ReplyPinPolicies.AsNoTracking()
            .AnyAsync(policy => policy.HostId == hostId, cancellationToken);

    private static BotAccountAuthorizationStatus ToAuthorizationStatus(
        HostBotAccountSettings settings,
        TokenStatus status
    ) =>
        status.Match<BotAccountAuthorizationStatus>(
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
                    unavailable.Reason
                    is AccessTokenUnavailableReason.CredentialProtectionUnavailable
                        ? "The custom bot credentials could not be used and were removed. Connect the custom bot again."
                        : "No custom bot account is connected yet."
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

    private static bool TokenExpiresSoon(HostBotAccountTokenPayload payload) =>
        payload.ExpiresAtUtc <= DateTimeOffset.UtcNow.Add(_refreshSkew);

    private static void ClearAuthorization(HostBotAccountSettings settings)
    {
        settings.AuthorizedAtUtc = null;
        settings.AuthorizedScopes = null;
        settings.DisplayName = null;
        settings.Login = null;
        settings.ProfileImageUrl = null;
        settings.ProtectedTokenPayload = null;
        settings.TwitchUserId = null;
    }

    private static TokenStatus ProtectionUnavailable(ImmutableArray<string> required) =>
        new TokenStatus.Unavailable(
            AccessTokenUnavailableReason.CredentialProtectionUnavailable,
            required
        );

    private static IEnumerable<string> SplitStoredScopes(string? scopes) =>
        (scopes ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);

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

    private static bool IsReady(TokenStatus status) =>
        status.Match(
            static _ => false,
            static _ => false,
            static _ => false,
            static _ => false,
            static _ => true
        );

    private static InvalidOperationException BotNotReady(string channelLogin) =>
        new($"The bot for #{channelLogin} is not ready yet.");

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

    private abstract record HostBotAccountTokenRefreshOutcome
    {
        private HostBotAccountTokenRefreshOutcome() { }

        public sealed record Refreshed(HostBotAccountTokenPayload Payload)
            : HostBotAccountTokenRefreshOutcome;

        public sealed record Rejected : HostBotAccountTokenRefreshOutcome;

        public sealed record ProtectionUnavailable(HostBotAccountTokenProtectionFailure Failure)
            : HostBotAccountTokenRefreshOutcome;

        public TResult Match<TResult>(
            Func<Refreshed, TResult> refreshed,
            Func<Rejected, TResult> rejected,
            Func<ProtectionUnavailable, TResult> protectionUnavailable
        ) =>
            this switch
            {
                Refreshed outcome => refreshed(outcome),
                Rejected outcome => rejected(outcome),
                ProtectionUnavailable outcome => protectionUnavailable(outcome),
                _ => throw new UnreachableException(),
            };
    }
}

public abstract record HostBotAccountAuthorizationOutcome
{
    private HostBotAccountAuthorizationOutcome() { }

    public sealed record Authorized : HostBotAccountAuthorizationOutcome;

    public sealed record HostNotFound : HostBotAccountAuthorizationOutcome;

    public sealed record OverrideDisabled : HostBotAccountAuthorizationOutcome;

    public sealed record AuthorityDenied : HostBotAccountAuthorizationOutcome;

    public sealed record MissingScopes(IReadOnlyList<string> Scopes)
        : HostBotAccountAuthorizationOutcome;

    public sealed record ProtectionUnavailable(HostBotAccountTokenProtectionFailure Failure)
        : HostBotAccountAuthorizationOutcome;
}

public abstract record HostBotAccountActor
{
    private HostBotAccountActor(string authenticatedUserId, string login)
    {
        AuthenticatedUserId = authenticatedUserId;
        Login = login;
    }

    public string AuthenticatedUserId { get; }

    public string Login { get; }

    public sealed record ChannelOwner : HostBotAccountActor
    {
        public ChannelOwner(string authenticatedUserId, string login)
            : base(authenticatedUserId, login) { }
    }

    public sealed record BotAdministrator : HostBotAccountActor
    {
        public BotAdministrator(string authenticatedUserId, string login)
            : base(authenticatedUserId, login) { }
    }
}

public abstract record HostBotAccountClearOutcome
{
    private HostBotAccountClearOutcome() { }

    public sealed record Cleared : HostBotAccountClearOutcome;

    public sealed record HostNotFound : HostBotAccountClearOutcome;

    public sealed record AuthorityDenied : HostBotAccountClearOutcome;
}
