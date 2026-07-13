namespace BlokeBot.Twitch.Runtime;

/// <summary>
/// Sends messages to Twitch chat channels.
/// </summary>
public interface IPublicChatMessageSender
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
    /// Matches the queue-admission outcome exhaustively.
    /// </summary>
    /// <typeparam name="TResult">The result produced by the selected handler.</typeparam>
    /// <param name="accepted">Handles durable queue acceptance.</param>
    /// <param name="rejected">Handles rejection before durable enqueue.</param>
    /// <returns>The selected handler's result.</returns>
    public abstract TResult Match<TResult>(
        Func<Accepted, TResult> accepted,
        Func<Rejected, TResult> rejected
    );

    /// <summary>
    /// The message was accepted into durable delivery processing.
    /// </summary>
    public sealed record Accepted : PublicChatSendOutcome
    {
        public override TResult Match<TResult>(
            Func<Accepted, TResult> accepted,
            Func<Rejected, TResult> rejected
        )
        {
            return accepted(this);
        }
    }

    /// <summary>
    /// Queue admission rejected the message before any durable write or delivery attempt.
    /// </summary>
    public sealed record Rejected : PublicChatSendOutcome
    {
        public override TResult Match<TResult>(
            Func<Accepted, TResult> accepted,
            Func<Rejected, TResult> rejected
        )
        {
            return rejected(this);
        }
    }
}
