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

public sealed record CommandResponse(CommandResponseTarget Target, string Message)
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
