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
    /// <param name="deadline">The explicit delivery-validity selection.</param>
    /// <param name="cancellationToken">A token that cancels the send operation.</param>
    /// <returns>The durable queue-admission outcome.</returns>
    ValueTask<PublicChatSendOutcome> SendAsync(
        string channel,
        string message,
        PublicChatDeliveryDeadline deadline,
        CancellationToken cancellationToken
    );
}

/// <summary>
/// Describes whether a public-chat message entered durable delivery processing.
/// </summary>
public abstract record PublicChatSendOutcome
{
    private PublicChatSendOutcome() { }

    /// <summary>
    /// The message was accepted into durable delivery processing.
    /// </summary>
    public sealed record Accepted : PublicChatSendOutcome;

    /// <summary>
    /// Queue admission rejected the message before any durable write or delivery attempt.
    /// </summary>
    public sealed record Rejected : PublicChatSendOutcome;
}
