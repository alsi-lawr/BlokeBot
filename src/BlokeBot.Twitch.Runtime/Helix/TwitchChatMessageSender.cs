using Microsoft.Extensions.Logging;

namespace BlokeBot.Twitch.Runtime;

internal sealed class TwitchChatMessageSender(
    TwitchAppAccessTokenProvider appTokens,
    ITwitchBotAccountProvider botAccounts,
    TwitchHelixChatClient helix,
    TwitchOutboundMessageQueue queue,
    ILogger<TwitchChatMessageSender> log
) : ITwitchChatMessageSender
{
    public async Task SendAsync(
        string channel,
        string message,
        CancellationToken cancellationToken
    ) => await queue.SendAsync(channel, message, SendNowAsync, cancellationToken);

    private async Task SendNowAsync(
        TwitchOutboundChatMessage message,
        CancellationToken cancellationToken
    )
    {
        var botAccount = await botAccounts.GetBotAccountAsync(message.Channel, cancellationToken);
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
            log.LogInformation("Sent Twitch chat message via Helix: {Message}", message.Message);
            return;
        }

        log.LogWarning(
            "Twitch dropped Helix chat message for #{Channel}. Code: {Code}; Message: {Message}",
            message.Channel,
            result.DropReason?.Code,
            result.DropReason?.Message
        );
    }
}
