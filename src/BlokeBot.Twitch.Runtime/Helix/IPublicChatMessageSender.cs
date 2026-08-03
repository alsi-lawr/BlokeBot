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

    ValueTask<PublicChatSendOutcome> SendCorrelatedAsync(
        string channel,
        string message,
        PublicChatDeliveryDeadline deadline,
        PublicChatDeliveryCorrelation correlation,
        CancellationToken cancellationToken
    ) => ValueTask.FromResult<PublicChatSendOutcome>(new PublicChatSendOutcome.Rejected());

    ValueTask<PublicChatSendOutcome> SendCorrelatedAsync(
        string channel,
        string message,
        PublicChatDeliveryDeadline deadline,
        PublicChatDeliveryCorrelation correlation,
        PublicChatPinIntent pinIntent,
        CancellationToken cancellationToken
    ) => ValueTask.FromResult<PublicChatSendOutcome>(new PublicChatSendOutcome.Rejected());

    ValueTask<PublicChatSendOutcome> SendAsync(
        string channel,
        string message,
        PublicChatDeliveryDeadline deadline,
        PublicChatPinIntent pinIntent,
        CancellationToken cancellationToken
    ) => SendAsync(channel, message, deadline, cancellationToken);
}

public sealed record PublicChatDeliveryCorrelation(int HostId, string ProviderMessageId)
{
    public PublicChatDeliveryCorrelation Validate()
    {
        if (HostId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(HostId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(ProviderMessageId);
        return ProviderMessageId.Length > 128
            ? throw new ArgumentOutOfRangeException(nameof(ProviderMessageId))
            : this;
    }
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
        ) => accepted(this);
    }

    /// <summary>
    /// Queue admission rejected the message before any durable write or delivery attempt.
    /// </summary>
    public sealed record Rejected : PublicChatSendOutcome
    {
        public override TResult Match<TResult>(
            Func<Accepted, TResult> accepted,
            Func<Rejected, TResult> rejected
        ) => rejected(this);
    }
}
