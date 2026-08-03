namespace BlokeBot.Commands;

public static class ChatModeratorPolicy
{
    public static bool IsModerator(ChatMessage message) =>
        string.Equals(message.Login, message.Channel, StringComparison.OrdinalIgnoreCase)
        || (message.Tags.TryGetValue("mod", out var mod) && mod == "1")
        || (
            message.Tags.TryGetValue("badges", out var badges)
            && badges
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(static x =>
                    x.StartsWith("broadcaster/", StringComparison.OrdinalIgnoreCase)
                    || x.StartsWith("moderator/", StringComparison.OrdinalIgnoreCase)
                )
        );
}
