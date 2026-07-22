namespace BlokeBot.Twitch.Runtime;

public abstract record PublicChatDeliveryDeadline
{
    private PublicChatDeliveryDeadline() { }

    public sealed record ConfiguredMaximum : PublicChatDeliveryDeadline;

    public sealed record ProducerAbsolute(DateTimeOffset ExpiresAt) : PublicChatDeliveryDeadline;
}

internal sealed record PublicChatEnqueueCommand
{
    public required string Channel { get; init; }

    public required string Message { get; init; }

    public required PublicChatDeliveryDeadline Deadline { get; init; }

    public PublicChatPinIntent? PinIntent { get; init; }
}
