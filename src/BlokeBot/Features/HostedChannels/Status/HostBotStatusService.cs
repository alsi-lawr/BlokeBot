using System.Text.Json;
using BlokeBot.Features.HostedChannels.Authorization;

namespace BlokeBot.Features.HostedChannels.Status;

public sealed class HostBotStatusService(
    IHostBotAppAccessTokenSource appTokens,
    IHostBotAccountTokenStatusProvider botAccounts,
    TwitchHelixApiClient helix,
    TwitchBotSettings settings
)
{
    public async Task<HostBotChannelStatus> GetStatusAsync(
        string channelLogin,
        CancellationToken ct
    )
    {
        return HostBotChannelStatus.FromReadiness(await GetReadinessAsync(channelLogin, ct));
    }

    public async Task<HostBotReadinessOutcome> GetReadinessAsync(
        string channelLogin,
        CancellationToken ct
    )
    {
        var configuredFlags = ConfiguredFlags();
        if (!HasAll(configuredFlags, HostBotChannelStatusFlags.ModeratorCheckConfigured))
        {
            return HostBotReadinessOutcome.NotConfigured();
        }

        try
        {
            return await EvaluateReadinessAsync(channelLogin, configuredFlags, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return HostBotReadinessOutcome.Unknown(configuredFlags);
        }
        catch (JsonException)
        {
            return HostBotReadinessOutcome.Unknown(configuredFlags);
        }
    }

    private async Task<HostBotReadinessOutcome> EvaluateReadinessAsync(
        string channelLogin,
        HostBotChannelStatusFlags configuredFlags,
        CancellationToken ct
    )
    {
        var tokenStatus = await botAccounts.GetActiveTokenStatusAsync(
            channelLogin,
            settings.Identity.Scopes,
            ct
        );
        return await tokenStatus.Status.Match(
            _ => Task.FromResult(HostBotReadinessOutcome.Unknown(configuredFlags)),
            _ => Task.FromResult(HostBotReadinessOutcome.TokenUnavailable(configuredFlags)),
            _ => Task.FromResult(HostBotReadinessOutcome.InvalidToken(configuredFlags)),
            missingScopes => EvaluateAuthorizedReadinessAsync(
                channelLogin,
                tokenStatus.BotLogin,
                missingScopes.AccessToken,
                missingScopes.Validation,
                missingScopes.GrantedScopes,
                configuredFlags,
                ct
            ),
            ready => EvaluateAuthorizedReadinessAsync(
                channelLogin,
                tokenStatus.BotLogin,
                ready.AccessToken,
                ready.Validation,
                ready.GrantedScopes,
                configuredFlags,
                ct
            )
        );
    }

    private async Task<HostBotReadinessOutcome> EvaluateAuthorizedReadinessAsync(
        string channelLogin,
        string botLogin,
        string accessToken,
        TwitchTokenValidation validation,
        IReadOnlyList<string> grantedScopes,
        HostBotChannelStatusFlags configuredFlags,
        CancellationToken ct
    )
    {
        var flags = GrantedFlags(configuredFlags, grantedScopes);
        if (!HasAll(flags, HostBotChannelStatusFlags.ModeratorCheckGranted))
        {
            return HostBotReadinessOutcome.MissingModeratorCheckScope(flags);
        }

        if (
            !string.IsNullOrWhiteSpace(botLogin)
            && !string.Equals(
                TwitchLogin.Normalize(botLogin),
                TwitchLogin.Normalize(validation.Login),
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return HostBotReadinessOutcome.BotAccountMismatch(flags);
        }

        var identities = await LookupUsersAsync(
            accessToken,
            [TwitchLogin.Normalize(channelLogin)],
            ct
        );
        if (!identities.TryGetValue(TwitchLogin.Normalize(channelLogin), out var channelId))
        {
            return HostBotReadinessOutcome.IdentityLookupFailed(flags);
        }

        if (string.Equals(validation.UserId, channelId, StringComparison.Ordinal))
        {
            return ChannelAuthorityReadyOutcome(flags);
        }

        var moderatorCheck = await helix.GetModeratedChannelStatusAsync(
            HelixContext(accessToken),
            validation.UserId,
            channelId,
            ct
        );
        return moderatorCheck switch
        {
            TwitchModeratedChannelStatus.IsModerator
                when HasAll(
                    flags,
                    HostBotChannelStatusFlags.FollowerReadConfigured
                        | HostBotChannelStatusFlags.FollowerReadGranted
                ) => HostBotReadinessOutcome.Ready(),
            TwitchModeratedChannelStatus.IsModerator =>
                HostBotReadinessOutcome.MissingFollowerReadScope(flags),
            TwitchModeratedChannelStatus.NotModerator => HostBotReadinessOutcome.NotModerator(
                flags
            ),
            TwitchModeratedChannelStatus.NeedsAuthorization =>
                HostBotReadinessOutcome.NeedsAuthorization(flags),
            TwitchModeratedChannelStatus.MissingPermission =>
                HostBotReadinessOutcome.MissingModeratorCheckPermission(flags),
            _ => HostBotReadinessOutcome.Unknown(flags),
        };
    }

    private static HostBotReadinessOutcome ChannelAuthorityReadyOutcome(
        HostBotChannelStatusFlags flags
    )
    {
        return HasAll(
            flags,
            HostBotChannelStatusFlags.FollowerReadConfigured
                | HostBotChannelStatusFlags.FollowerReadGranted
        )
            ? HostBotReadinessOutcome.Ready()
            : HostBotReadinessOutcome.MissingFollowerReadScope(flags);
    }

    public async Task<HostStreamLivenessOutcome> GetStreamLivenessAsync(
        string channelLogin,
        CancellationToken ct
    )
    {
        try
        {
            var token = await appTokens.GetAccessTokenAsync(ct);
            return await helix.IsStreamLiveAsync(HelixContext(token), channelLogin, ct)
                ? new HostStreamLivenessOutcome.Live()
                : new HostStreamLivenessOutcome.Offline();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (HostBotAppAccessTokenUnavailableException exception)
        {
            return Unavailable(
                HostStreamLivenessUnavailableReason.AppAccessTokenUnavailable,
                exception
            );
        }
        catch (TwitchAppAccessTokenResponseException exception)
        {
            return Unavailable(
                HostStreamLivenessUnavailableReason.ProviderResponseInvalid,
                exception
            );
        }
        catch (HttpRequestException exception)
        {
            return Unavailable(
                HostStreamLivenessUnavailableReason.ProviderRequestFailed,
                exception
            );
        }
        catch (IOException exception)
        {
            return Unavailable(
                HostStreamLivenessUnavailableReason.ProviderRequestFailed,
                exception
            );
        }
        catch (JsonException exception)
        {
            return Unavailable(
                HostStreamLivenessUnavailableReason.ProviderResponseInvalid,
                exception
            );
        }
        catch (TimeoutException exception)
        {
            return Unavailable(
                HostStreamLivenessUnavailableReason.ProviderTimedOut,
                exception
            );
        }
        catch (OperationCanceledException exception)
        {
            return Unavailable(
                HostStreamLivenessUnavailableReason.ProviderTimedOut,
                exception
            );
        }
    }

    public async Task<FollowerCheckResult> IsFollowerAsync(
        string channelLogin,
        string viewerLogin,
        CancellationToken ct
    )
    {
        var status = await GetStatusAsync(channelLogin, ct);
        if (status.ModeratorState != HostBotModeratorState.IsModerator)
        {
            return FollowerCheckResult.Unavailable;
        }

        var tokenStatus = await GetValidatedUserAccessTokenAsync(channelLogin, ct);
        var token = tokenStatus.AccessToken;
        var identities = await LookupUsersAsync(
            token,
            [TwitchLogin.Normalize(channelLogin), TwitchLogin.Normalize(viewerLogin)],
            ct
        );
        if (
            !identities.TryGetValue(TwitchLogin.Normalize(channelLogin), out var channelId)
            || !identities.TryGetValue(TwitchLogin.Normalize(viewerLogin), out var viewerId)
        )
        {
            return FollowerCheckResult.NotEligible;
        }

        return await helix.GetFollowerStatusAsync(
            HelixContext(token),
            channelId,
            viewerId,
            tokenStatus.Validation.UserId,
            ct
        ) switch
        {
            TwitchFollowerStatus.Follows => FollowerCheckResult.Eligible,
            TwitchFollowerStatus.DoesNotFollow => FollowerCheckResult.NotEligible,
            _ => FollowerCheckResult.Unavailable,
        };
    }

    private HostBotChannelStatusFlags ConfiguredFlags()
    {
        return settings
            .Identity.Scopes.Select(TwitchScopeSet.Normalize)
            .Aggregate(
                HostBotChannelStatusFlags.None,
                (flags, scope) =>
                    flags
                    | (
                        scope switch
                        {
                            TwitchScopes.UserReadModeratedChannels =>
                                HostBotChannelStatusFlags.ModeratorCheckConfigured,
                            TwitchScopes.ModeratorReadFollowers =>
                                HostBotChannelStatusFlags.FollowerReadConfigured,
                            _ => HostBotChannelStatusFlags.None,
                        }
                    )
            );
    }

    private static HostBotChannelStatusFlags GrantedFlags(
        HostBotChannelStatusFlags configuredFlags,
        IReadOnlyList<string> grantedScopes
    )
    {
        return grantedScopes
            .Select(TwitchScopeSet.Normalize)
            .Aggregate(
                configuredFlags | HostBotChannelStatusFlags.BotAccountAuthorized,
                (flags, scope) =>
                    flags
                    | (
                        scope switch
                        {
                            TwitchScopes.UserReadModeratedChannels =>
                                HostBotChannelStatusFlags.ModeratorCheckGranted,
                            TwitchScopes.ModeratorReadFollowers =>
                                HostBotChannelStatusFlags.FollowerReadGranted,
                            _ => HostBotChannelStatusFlags.None,
                        }
                    )
            );
    }

    private static bool HasAll(
        HostBotChannelStatusFlags flags,
        HostBotChannelStatusFlags required
    )
    {
        return (flags & required) == required;
    }

    private async Task<ValidatedUserAccessToken> GetValidatedUserAccessTokenAsync(
        string channelLogin,
        CancellationToken ct
    )
    {
        var status = await botAccounts.GetActiveTokenStatusAsync(
            channelLogin,
            settings.Identity.Scopes,
            ct
        );
        return status.Status.Match(
            _ => throw BotRunnerNotConnected(),
            _ => throw BotRunnerNotConnected(),
            _ => throw BotRunnerNotConnected(),
            missingScopes => new ValidatedUserAccessToken
            {
                AccessToken = missingScopes.AccessToken,
                Validation = missingScopes.Validation,
            },
            ready => new ValidatedUserAccessToken
            {
                AccessToken = ready.AccessToken,
                Validation = ready.Validation,
            }
        );
    }

    private async Task<Dictionary<string, string>> LookupUsersAsync(
        string token,
        IReadOnlyList<string> logins,
        CancellationToken ct
    )
    {
        var users = await helix.GetUsersByLoginAsync(HelixContext(token), logins, ct);
        return users.ToDictionary(
            x => TwitchLogin.Normalize(x.Login),
            x => x.Id,
            StringComparer.OrdinalIgnoreCase
        );
    }

    private TwitchHelixRequestContext HelixContext(string token)
    {
        return new(settings.Identity.ClientId, token);
    }

    private static HostStreamLivenessOutcome.Unavailable Unavailable(
        HostStreamLivenessUnavailableReason reason,
        Exception cause
    )
    {
        return new(reason, cause);
    }

    private static InvalidOperationException BotRunnerNotConnected()
    {
        return new("The Twitch bot runner is not connected.");
    }

    private sealed record ValidatedUserAccessToken
    {
        internal required string AccessToken { get; init; }

        internal required TwitchTokenValidation Validation { get; init; }
    }
}
