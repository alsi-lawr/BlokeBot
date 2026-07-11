namespace BlokeBot.Commands;

/// <summary>
/// Handles a typed response produced by a Twitch command.
/// </summary>
/// <param name="response">The response to deliver.</param>
/// <param name="cancellationToken">A token that cancels response delivery.</param>
public delegate ValueTask TwitchCommandResponder(
    TwitchCommandResponse response,
    CancellationToken cancellationToken
);

public enum TwitchCommandResponseTarget
{
    Chat,
    Whisper,
}

public sealed record TwitchCommandResponse(TwitchCommandResponseTarget Target, string Message)
{
    public static TwitchCommandResponse Chat(string message) =>
        new(TwitchCommandResponseTarget.Chat, message);

    public static TwitchCommandResponse Whisper(string message) =>
        new(TwitchCommandResponseTarget.Whisper, message);
}
