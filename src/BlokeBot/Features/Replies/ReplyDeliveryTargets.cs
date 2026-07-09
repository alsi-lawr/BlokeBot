namespace BlokeBot.Features.Replies;

public static class ReplyDeliveryTargets
{
    public const string Chat = "chat";
    public const string Whisper = "whisper";

    public static TwitchCommandResponseTarget ToCommandTarget(string? target) =>
        string.Equals(target, Whisper, StringComparison.OrdinalIgnoreCase)
            ? TwitchCommandResponseTarget.Whisper
            : TwitchCommandResponseTarget.Chat;

    public static string FromCommandTarget(TwitchCommandResponseTarget target) =>
        target == TwitchCommandResponseTarget.Whisper ? Whisper : Chat;
}
