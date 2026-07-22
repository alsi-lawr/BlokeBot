namespace BlokeBot.Commands;

/// <summary>
/// Handles a typed response produced by a Twitch command.
/// </summary>
/// <param name="response">The response to deliver.</param>
/// <param name="cancellationToken">A token that cancels response delivery.</param>
public delegate ValueTask CommandResponder(
    CommandResponse response,
    CancellationToken cancellationToken
);

public enum CommandResponseTarget
{
    Chat,
    Whisper,
}

public sealed record CommandResponse(
    CommandResponseTarget Target,
    string Message,
    PublicChatPinIntent? Pin = null
)
{
    public static CommandResponse Chat(string message)
    {
        return new(CommandResponseTarget.Chat, message);
    }

    public static CommandResponse Whisper(string message)
    {
        return new(CommandResponseTarget.Whisper, message);
    }
}

public sealed record PublicChatPinIntent(
    int HostId,
    long OwnerId,
    string Feature,
    string ReplyKey,
    int? DurationSeconds,
    bool UnpinOnOwnerCompletion
)
{
    public PublicChatPinIntent Validate()
    {
        if (HostId <= 0 || OwnerId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(HostId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(Feature);
        ArgumentException.ThrowIfNullOrWhiteSpace(ReplyKey);
        if (DurationSeconds is { } seconds && seconds is < 30 or > 1800)
        {
            throw new ArgumentOutOfRangeException(nameof(DurationSeconds));
        }

        return this;
    }
}
