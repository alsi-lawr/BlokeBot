using Microsoft.Extensions.Logging;

namespace BlokeBot.Twitch.Runtime;

internal sealed class PublicChatCommandResponseSender(
    IPublicChatMessageSender sender,
    ILogger<PublicChatCommandResponseSender> log
) : ICommandResponseSender
{
    public async ValueTask SendAsync(
        ChatMessage sourceMessage,
        CommandResponse response,
        CancellationToken cancellationToken
    )
    {
        if (response.Target == CommandResponseTarget.Whisper)
        {
            log.LogWarning(
                "Private command response delivery is unavailable in public-chat-only mode for host channel #{HostChannel}; no user-visible delivery was attempted.",
                Login.Normalize(sourceMessage.Channel)
            );
            return;
        }

        var outcome = await sender.SendAsync(
            sourceMessage.Channel,
            response.Message,
            new PublicChatDeliveryDeadline.ConfiguredMaximum(),
            cancellationToken
        );
        outcome
            .Match<Action>(
                static _ => static () => { },
                _ =>
                    () =>
                        log.LogWarning(
                            "Public command response for host channel #{HostChannel} was rejected before durable enqueue; no user-visible delivery was attempted.",
                            Login.Normalize(sourceMessage.Channel)
                        )
            )
            .Invoke();
    }
}
