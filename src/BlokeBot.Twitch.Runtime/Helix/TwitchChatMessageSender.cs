namespace BlokeBot.Twitch.Runtime;

internal sealed class TwitchChatMessageSender(
    PublicChatMessageQueue queue
) : ITwitchChatMessageSender
{
    public async Task SendAsync(
        string channel,
        string message,
        CancellationToken cancellationToken
    )
    {
        _ = await queue.EnqueueAsync(channel, message, cancellationToken);
    }
}
