using Microsoft.Extensions.Options;

namespace BlokeBot.Features.HostedChannels.Status;

public sealed class HostBotStatusService(
    IServiceProvider services,
    TwitchOAuthApiClient oauth,
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

        string token;
        try
        {
            token = await GetUserAccessTokenAsync(ct);
        }
        catch (InvalidOperationException)
        {
            return HostBotChannelStatus.NeedsAuthorization(flags);
        }

        try
        {
            var validation = await oauth.ValidateTokenAsync(token, ct);
            if (validation is null)
                return HostBotChannelStatus.NeedsAuthorization(flags);

            flags |= HostBotChannelStatusFlags.BotAccountAuthorized;
            if (validation.Scopes.Contains(TwitchScopes.UserReadModeratedChannels))
                flags |= HostBotChannelStatusFlags.ModeratorCheckGranted;
            if (validation.Scopes.Contains(TwitchScopes.ModeratorReadFollowers))
                flags |= HostBotChannelStatusFlags.FollowerReadGranted;

            if ((flags & HostBotChannelStatusFlags.ModeratorCheckGranted) == 0)
                return HostBotChannelStatus.MissingModeratorCheckPermission(flags);

            var identities = await LookupUsersAsync(
                token,
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

            if (!string.Equals(validation.UserId, botId, StringComparison.Ordinal))
                return HostBotChannelStatus.NeedsAuthorization(flags);

            var moderatorCheck = await helix.GetModeratedChannelStatusAsync(
                HelixContext(token),
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

        var token = await GetUserAccessTokenAsync(ct);
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

    private async Task<string> GetUserAccessTokenAsync(CancellationToken ct)
    {
        var userToken = services.GetService<ITwitchAccessTokenProvider>();
        if (userToken is null)
            throw new InvalidOperationException("Twitch bot runtime is not configured.");

        return await userToken.GetAccessTokenAsync(ct);
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
