namespace BlokeBot.Commands;

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
