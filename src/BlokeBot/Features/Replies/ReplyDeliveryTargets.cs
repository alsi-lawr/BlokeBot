namespace BlokeBot.Features.Replies;

public static class ReplyDeliveryTargets
{
    public const string Chat = "chat";
    public const string Whisper = "whisper";

    public static CommandResponseTarget ToCommandTarget(string? target)
    {
        return string.Equals(target, Whisper, StringComparison.OrdinalIgnoreCase)
            ? CommandResponseTarget.Whisper
            : CommandResponseTarget.Chat;
    }

    public static string FromCommandTarget(CommandResponseTarget target)
    {
        return target == CommandResponseTarget.Whisper ? Whisper : Chat;
    }
}
