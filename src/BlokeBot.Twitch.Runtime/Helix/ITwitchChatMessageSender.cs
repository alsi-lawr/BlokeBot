namespace BlokeBot.Twitch.Runtime;

/// <summary>
/// Sends messages to Twitch chat channels.
/// </summary>
public interface ITwitchChatMessageSender
{
    /// <summary>
    /// Sends a chat message as the configured bot account.
    /// </summary>
    /// <param name="channel">The target channel login, without a leading hash character.</param>
    /// <param name="message">The chat message text.</param>
    /// <param name="cancellationToken">A token that cancels the send operation.</param>
    /// <returns>A task that completes when Twitch accepts or drops the message.</returns>
    Task SendAsync(string channel, string message, CancellationToken cancellationToken);
}
