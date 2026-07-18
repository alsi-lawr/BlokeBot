using System.Diagnostics;
using System.Text.Json;
using BlokeBot.Core.Features.HostedChannels.Authorization;

namespace BlokeBot.Core.Features.CustomCommands;

public interface ITwitchAnnouncementReadinessProvider
{
    Task<TwitchAnnouncementReadiness> GetReadinessAsync(
        string channelLogin,
        CancellationToken cancellationToken
    );
}

internal interface ITwitchAnnouncementAccessService : ITwitchAnnouncementReadinessProvider
{
    Task<TwitchAnnouncementAccess> GetAccessAsync(
        string channelLogin,
        CancellationToken cancellationToken
    );
}

internal sealed class TwitchAnnouncementAccessService(
    IHostBotAccountTokenStatusProvider botAccounts,
    HelixClient helix,
    BotSettings settings
) : ITwitchAnnouncementAccessService
{
    public async Task<TwitchAnnouncementReadiness> GetReadinessAsync(
        string channelLogin,
        CancellationToken cancellationToken
    )
    {
        var access = await GetAccessAsync(channelLogin, cancellationToken);
        return access switch
        {
            TwitchAnnouncementAccess.Ready ready => new(
                TwitchAnnouncementAvailability.Available,
                ready.BotLogin
            ),
            TwitchAnnouncementAccess.ReconnectRequired reconnect => new(
                TwitchAnnouncementAvailability.ReconnectRequired,
                reconnect.BotLogin
            ),
            TwitchAnnouncementAccess.AuthorityRequired authority => new(
                TwitchAnnouncementAvailability.AuthorityRequired,
                authority.BotLogin
            ),
            TwitchAnnouncementAccess.Unavailable unavailable => new(
                TwitchAnnouncementAvailability.Unavailable,
                unavailable.BotLogin
            ),
            _ => throw new UnreachableException("Unknown Twitch announcement access."),
        };
    }

    public async Task<TwitchAnnouncementAccess> GetAccessAsync(
        string channelLogin,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var tokenStatus = await botAccounts.GetActiveTokenStatusAsync(
                channelLogin,
                [Scopes.ModeratorManageAnnouncements, Scopes.UserReadModeratedChannels],
                cancellationToken
            );
            return await tokenStatus.Status.Match<Task<TwitchAnnouncementAccess>>(
                _ =>
                    Task.FromResult<TwitchAnnouncementAccess>(
                        new TwitchAnnouncementAccess.Unavailable(tokenStatus.BotLogin)
                    ),
                _ =>
                    Task.FromResult<TwitchAnnouncementAccess>(
                        new TwitchAnnouncementAccess.ReconnectRequired(tokenStatus.BotLogin)
                    ),
                _ =>
                    Task.FromResult<TwitchAnnouncementAccess>(
                        new TwitchAnnouncementAccess.ReconnectRequired(tokenStatus.BotLogin)
                    ),
                _ =>
                    Task.FromResult<TwitchAnnouncementAccess>(
                        new TwitchAnnouncementAccess.ReconnectRequired(tokenStatus.BotLogin)
                    ),
                ready =>
                    ResolveAuthorizedAsync(
                        channelLogin,
                        tokenStatus.BotLogin,
                        ready.AccessToken,
                        ready.Validation,
                        cancellationToken
                    )
            );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new TwitchAnnouncementAccess.Unavailable(string.Empty);
        }
        catch (JsonException)
        {
            return new TwitchAnnouncementAccess.Unavailable(string.Empty);
        }
        catch (IOException)
        {
            return new TwitchAnnouncementAccess.Unavailable(string.Empty);
        }
        catch (TimeoutException)
        {
            return new TwitchAnnouncementAccess.Unavailable(string.Empty);
        }
    }

    private async Task<TwitchAnnouncementAccess> ResolveAuthorizedAsync(
        string channelLogin,
        string botLogin,
        string accessToken,
        TokenValidation validation,
        CancellationToken cancellationToken
    )
    {
        if (
            !string.IsNullOrWhiteSpace(botLogin)
            && !string.Equals(
                Login.Normalize(botLogin),
                Login.Normalize(validation.Login),
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return new TwitchAnnouncementAccess.Unavailable(botLogin);
        }

        var channel = (
            await helix.GetUsersByLoginAsync(
                HelixContext(accessToken),
                [Login.Normalize(channelLogin)],
                cancellationToken
            )
        ).SingleOrDefault();
        if (channel is null)
        {
            return new TwitchAnnouncementAccess.Unavailable(botLogin);
        }

        if (string.Equals(validation.UserId, channel.Id, StringComparison.Ordinal))
        {
            return new TwitchAnnouncementAccess.Ready(
                botLogin,
                HelixContext(accessToken),
                channel.Id,
                validation.UserId
            );
        }

        var moderatorStatus = await helix.GetModeratedChannelStatusAsync(
            HelixContext(accessToken),
            validation.UserId,
            channel.Id,
            cancellationToken
        );
        return moderatorStatus switch
        {
            ModeratedChannelStatus.IsModerator => new TwitchAnnouncementAccess.Ready(
                botLogin,
                HelixContext(accessToken),
                channel.Id,
                validation.UserId
            ),
            ModeratedChannelStatus.NotModerator => new TwitchAnnouncementAccess.AuthorityRequired(
                botLogin
            ),
            ModeratedChannelStatus.MissingPermission =>
                new TwitchAnnouncementAccess.ReconnectRequired(botLogin),
            ModeratedChannelStatus.Unknown or ModeratedChannelStatus.NeedsAuthorization =>
                new TwitchAnnouncementAccess.Unavailable(botLogin),
            _ => throw new UnreachableException("Unknown moderated channel status."),
        };
    }

    private HelixRequestContext HelixContext(string accessToken)
    {
        return new(settings.Identity.ClientId, accessToken);
    }
}

internal abstract class TwitchAnnouncementAccess
{
    private TwitchAnnouncementAccess(string botLogin)
    {
        BotLogin = botLogin;
    }

    public string BotLogin { get; }

    internal sealed class Ready(
        string botLogin,
        HelixRequestContext context,
        string broadcasterId,
        string moderatorId
    ) : TwitchAnnouncementAccess(botLogin)
    {
        public HelixRequestContext Context { get; } = context;

        public string BroadcasterId { get; } = broadcasterId;

        public string ModeratorId { get; } = moderatorId;
    }

    internal sealed class ReconnectRequired(string botLogin) : TwitchAnnouncementAccess(botLogin);

    internal sealed class AuthorityRequired(string botLogin) : TwitchAnnouncementAccess(botLogin);

    internal sealed class Unavailable(string botLogin) : TwitchAnnouncementAccess(botLogin);
}
