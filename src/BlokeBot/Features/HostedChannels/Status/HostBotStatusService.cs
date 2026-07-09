using System.Text.Json;
using BlokeBot.Features.HostedChannels.Authorization;
using Microsoft.Extensions.Options;

namespace BlokeBot.Features.HostedChannels.Status;

public sealed class HostBotStatusService(
    IServiceProvider services,
    HostBotAccountAuthorizationService botAccounts,
    TwitchHelixApiClient helix,
    IOptions<TwitchBotOptions> options
)
{
    private readonly TwitchBotOptions options = options.Value;

    public async Task<HostBotChannelStatus> GetStatusAsync(
        string channelLogin,
        CancellationToken ct
    ) => HostBotChannelStatus.FromReadiness(await GetReadinessAsync(channelLogin, ct));

    public async Task<HostBotReadinessOutcome> GetReadinessAsync(
        string channelLogin,
        CancellationToken ct
    )
    {
        var configuredFlags = ConfiguredFlags();
        if (!HasAll(configuredFlags, HostBotChannelStatusFlags.ModeratorCheckConfigured))
            return HostBotReadinessOutcome.NotConfigured();

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
            options.Identity.Scopes,
            ct
        );
        return tokenStatus.State switch
        {
            TwitchTokenStatusState.Unavailable => HostBotReadinessOutcome.TokenUnavailable(
                configuredFlags
            ),
            TwitchTokenStatusState.Invalid => HostBotReadinessOutcome.InvalidToken(configuredFlags),
            TwitchTokenStatusState.Unknown => HostBotReadinessOutcome.Unknown(configuredFlags),
            _ when tokenStatus.AccessToken is null || tokenStatus.Validation is null =>
                HostBotReadinessOutcome.Unknown(configuredFlags),
            _ => await EvaluateAuthorizedReadinessAsync(
                channelLogin,
                tokenStatus,
                configuredFlags,
                ct
            ),
        };
    }

    private async Task<HostBotReadinessOutcome> EvaluateAuthorizedReadinessAsync(
        string channelLogin,
        ActiveBotAccountTokenStatus tokenStatus,
        HostBotChannelStatusFlags configuredFlags,
        CancellationToken ct
    )
    {
        var flags = GrantedFlags(configuredFlags, tokenStatus.GrantedScopes);
        if (!HasAll(flags, HostBotChannelStatusFlags.ModeratorCheckGranted))
            return HostBotReadinessOutcome.MissingModeratorCheckScope(flags);

        var identities = await LookupUsersAsync(
            tokenStatus.AccessToken!,
            [
                TwitchLogin.Normalize(channelLogin),
                TwitchLogin.Normalize(tokenStatus.Validation!.Login),
            ],
            ct
        );
        if (
            !identities.TryGetValue(TwitchLogin.Normalize(channelLogin), out var channelId)
            || !identities.TryGetValue(
                TwitchLogin.Normalize(tokenStatus.Validation!.Login),
                out var botId
            )
        )
        {
            return HostBotReadinessOutcome.IdentityLookupFailed(flags);
        }

        if (!string.Equals(tokenStatus.Validation!.UserId, botId, StringComparison.Ordinal))
            return HostBotReadinessOutcome.BotAccountMismatch(flags);

        var moderatorCheck = await helix.GetModeratedChannelStatusAsync(
            HelixContext(tokenStatus.AccessToken!),
            botId,
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

    public async Task<bool> IsStreamLiveAsync(string channelLogin, CancellationToken ct)
    {
        var token = await GetAppTokenAsync(ct);
        return await helix.IsStreamLiveAsync(HelixContext(token), channelLogin, ct);
    }

    public async Task<FollowerCheckResult> IsFollowerAsync(
        string channelLogin,
        string viewerLogin,
        CancellationToken ct
    )
    {
        var status = await GetStatusAsync(channelLogin, ct);
        if (status.ModeratorState != HostBotModeratorState.IsModerator)
            return FollowerCheckResult.Unavailable;

        var tokenStatus = await GetValidatedUserAccessTokenAsync(channelLogin, ct);
        var token = tokenStatus.AccessToken!;
        var identities = await LookupUsersAsync(
            token,
            [
                TwitchLogin.Normalize(channelLogin),
                TwitchLogin.Normalize(viewerLogin),
                TwitchLogin.Normalize(tokenStatus.Validation!.Login),
            ],
            ct
        );
        if (
            !identities.TryGetValue(TwitchLogin.Normalize(channelLogin), out var channelId)
            || !identities.TryGetValue(TwitchLogin.Normalize(viewerLogin), out var viewerId)
            || !identities.TryGetValue(
                TwitchLogin.Normalize(tokenStatus.Validation!.Login),
                out var botId
            )
        )
        {
            return FollowerCheckResult.NotEligible;
        }

        return await helix.GetFollowerStatusAsync(
            HelixContext(token),
            channelId,
            viewerId,
            botId,
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
        return options
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
    ) => (flags & required) == required;

    private async Task<string> GetAppTokenAsync(CancellationToken ct)
    {
        var appTokens = services.GetService<TwitchAppAccessTokenProvider>();
        if (appTokens is null)
            throw new InvalidOperationException("Twitch bot runtime is not configured.");

        return await appTokens.GetAccessTokenAsync(ct);
    }

    private async Task<ActiveBotAccountTokenStatus> GetValidatedUserAccessTokenAsync(
        string channelLogin,
        CancellationToken ct
    )
    {
        var status = await botAccounts.GetActiveTokenStatusAsync(
            channelLogin,
            options.Identity.Scopes,
            ct
        );
        if (status.AccessToken is not null && status.Validation is not null)
            return status;

        throw new InvalidOperationException("Twitch bot runtime is not authorized.");
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

    private TwitchHelixRequestContext HelixContext(string token) =>
        new(options.Identity.ClientId, token);
}
