using Microsoft.Extensions.Logging;

namespace BlokeBot.Twitch.Runtime;

internal sealed class TwitchChatCommandResponseSender(
    ITwitchChatMessageSender sender,
    ILogger<TwitchChatCommandResponseSender> log
) : ITwitchCommandResponseSender
{
    public async ValueTask SendAsync(
        TwitchChatMessage sourceMessage,
        TwitchCommandResponse response,
        CancellationToken cancellationToken
    )
    {
        if (response.Target == TwitchCommandResponseTarget.Whisper)
        {
            log.LogInformation(
                "Whisper response requested for {Login} in #{Channel}, but no whisper sender is registered. Falling back to chat.",
                sourceMessage.Login,
                sourceMessage.Channel
            );
        }

        await sender.SendAsync(
            sourceMessage.Channel,
            response.Message,
            new PublicChatDeliveryDeadline.ConfiguredMaximum(),
            cancellationToken
        );
    }
}
