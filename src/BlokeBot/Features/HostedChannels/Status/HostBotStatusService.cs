using Microsoft.Extensions.Options;

namespace BlokeBot.Features.HostedChannels.Status;

public sealed class HostBotStatusService(
    IServiceProvider services,
    TwitchTokenStatusService tokens,
    TwitchHelixApiClient helix,
    IOptions<TwitchBotOptions> options
)
{
    private readonly TwitchBotOptions options = options.Value;

    public async Task<HostBotChannelStatus> GetStatusAsync(
        string channelLogin,
        CancellationToken ct
    )
    {
        var flags = ConfiguredFlags();
        if ((flags & HostBotChannelStatusFlags.ModeratorCheckConfigured) == 0)
            return HostBotChannelStatus.NotConfigured();

        try
        {
            var tokenStatus = await tokens.GetUserAccessTokenStatusAsync(
                options.Identity.Scopes,
                ct
            );
            if (tokenStatus.State == TwitchTokenStatusState.Unavailable
                || tokenStatus.State == TwitchTokenStatusState.Invalid)
            {
                return HostBotChannelStatus.NeedsAuthorization(flags);
            }

            if (tokenStatus.AccessToken is null || tokenStatus.Validation is null)
                return HostBotChannelStatus.Unknown(flags);

            flags |= HostBotChannelStatusFlags.BotAccountAuthorized;
            if (tokenStatus.GrantedScopes.Contains(TwitchScopes.UserReadModeratedChannels))
                flags |= HostBotChannelStatusFlags.ModeratorCheckGranted;
            if (tokenStatus.GrantedScopes.Contains(TwitchScopes.ModeratorReadFollowers))
                flags |= HostBotChannelStatusFlags.FollowerReadGranted;

            if ((flags & HostBotChannelStatusFlags.ModeratorCheckGranted) == 0)
                return HostBotChannelStatus.MissingModeratorCheckPermission(flags);

            var identities = await LookupUsersAsync(
                tokenStatus.AccessToken,
                [TwitchLogin.Normalize(channelLogin), TwitchLogin.Normalize(options.Identity.BotUsername)],
                ct
            );
            if (
                !identities.TryGetValue(TwitchLogin.Normalize(channelLogin), out var channelId)
                || !identities.TryGetValue(
                    TwitchLogin.Normalize(options.Identity.BotUsername),
                    out var botId
                )
            )
            {
                return HostBotChannelStatus.Unknown(flags);
            }

            if (!string.Equals(tokenStatus.Validation.UserId, botId, StringComparison.Ordinal))
                return HostBotChannelStatus.NeedsAuthorization(flags);

            var moderatorCheck = await helix.GetModeratedChannelStatusAsync(
                HelixContext(tokenStatus.AccessToken),
                botId,
                channelId,
                ct
            );
            return moderatorCheck switch
            {
                TwitchModeratedChannelStatus.IsModerator
                    when (
                        flags
                        & (
                            HostBotChannelStatusFlags.FollowerReadConfigured
                            | HostBotChannelStatusFlags.FollowerReadGranted
                        )
                    )
                        == (
                            HostBotChannelStatusFlags.FollowerReadConfigured
                            | HostBotChannelStatusFlags.FollowerReadGranted
                        ) => HostBotChannelStatus.Ready(),
                TwitchModeratedChannelStatus.IsModerator =>
                    HostBotChannelStatus.MissingFollowerReadPermission(flags),
                TwitchModeratedChannelStatus.NotModerator => HostBotChannelStatus.NotModerator(flags),
                TwitchModeratedChannelStatus.NeedsAuthorization => HostBotChannelStatus.NeedsAuthorization(
                    flags
                ),
                TwitchModeratedChannelStatus.MissingPermission =>
                    HostBotChannelStatus.MissingModeratorCheckPermission(flags),
                _ => HostBotChannelStatus.Unknown(flags),
            };
        }
        catch
        {
            return HostBotChannelStatus.Unknown(flags);
        }
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

        var token = await GetValidatedUserAccessTokenAsync(ct);
        var identities = await LookupUsersAsync(
            token,
            [
                TwitchLogin.Normalize(channelLogin),
                TwitchLogin.Normalize(viewerLogin),
                TwitchLogin.Normalize(options.Identity.BotUsername),
            ],
            ct
        );
        if (
            !identities.TryGetValue(TwitchLogin.Normalize(channelLogin), out var channelId)
            || !identities.TryGetValue(TwitchLogin.Normalize(viewerLogin), out var viewerId)
            || !identities.TryGetValue(TwitchLogin.Normalize(options.Identity.BotUsername), out var botId)
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
        var flags = HostBotChannelStatusFlags.None;
        foreach (var scope in options.Identity.Scopes.Select(TwitchScopeSet.Normalize))
        {
            flags |= scope switch
            {
                TwitchScopes.UserReadModeratedChannels =>
                    HostBotChannelStatusFlags.ModeratorCheckConfigured,
                TwitchScopes.ModeratorReadFollowers =>
                    HostBotChannelStatusFlags.FollowerReadConfigured,
                _ => HostBotChannelStatusFlags.None,
            };
        }

        return flags;
    }

    private async Task<string> GetAppTokenAsync(CancellationToken ct)
    {
        var appTokens = services.GetService<TwitchAppAccessTokenProvider>();
        if (appTokens is null)
            throw new InvalidOperationException("Twitch bot runtime is not configured.");

        return await appTokens.GetAccessTokenAsync(ct);
    }

    private async Task<string> GetValidatedUserAccessTokenAsync(CancellationToken ct)
    {
        var status = await tokens.GetUserAccessTokenStatusAsync(options.Identity.Scopes, ct);
        if (status.AccessToken is not null && status.Validation is not null)
            return status.AccessToken;

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
