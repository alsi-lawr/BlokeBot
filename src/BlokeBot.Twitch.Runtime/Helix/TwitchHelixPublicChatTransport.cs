using Microsoft.Extensions.Logging;

namespace BlokeBot.Twitch.Runtime;

internal interface IPublicChatTransport
{
    ValueTask SendAsync(
        PublicChatClaimedMessage message,
        CancellationToken cancellationToken
    );
}

internal sealed class TwitchHelixPublicChatTransport(
    TwitchAppAccessTokenProvider appTokens,
    ITwitchBotAccountProvider botAccounts,
    TwitchHelixChatClient helix,
    ILogger<TwitchHelixPublicChatTransport> log
) : IPublicChatTransport
{
    public async ValueTask SendAsync(
        PublicChatClaimedMessage message,
        CancellationToken cancellationToken
    )
    {
        var botAccount = await botAccounts.GetBotAccountAsync(
            message.Channel,
            cancellationToken
        );
        var identities = await helix.ResolveChatIdentitiesAsync(
            message.Channel,
            botAccount.Login,
            botAccount.AccessToken,
            cancellationToken
        );
        var appAccessToken = await appTokens.GetAccessTokenAsync(cancellationToken);
        var result = await helix.SendChatMessageAsync(
            appAccessToken,
            identities.BroadcasterId,
            identities.BotUserId,
            message.Message,
            cancellationToken
        );

        if (result.IsSent)
        {
            log.LogInformation(
                "Sent public chat outbox message {OutboxMessageId} via Helix in #{Channel}.",
                message.Id,
                message.Channel
            );
            return;
        }

        log.LogWarning(
            "Twitch rejected public chat outbox message {OutboxMessageId} in #{Channel} with code {Code}.",
            message.Id,
            message.Channel,
            result.DropReason?.Code
        );
    }
}
