namespace BlokeBot.Commands;

public static class TwitchModeratorPolicy
{
    public static bool IsModerator(ChatMessage message)
    {
        if (string.Equals(message.Login, message.Channel, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (message.Tags.TryGetValue("mod", out var mod) && mod == "1")
        {
            return true;
        }

        if (!message.Tags.TryGetValue("badges", out var badges))
        {
            return false;
        }

        return badges
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(x =>
                x.StartsWith("broadcaster/", StringComparison.OrdinalIgnoreCase)
                || x.StartsWith("moderator/", StringComparison.OrdinalIgnoreCase)
            );
    }
}
