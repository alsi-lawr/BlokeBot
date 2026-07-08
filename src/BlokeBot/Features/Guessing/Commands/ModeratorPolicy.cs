using Alsi.TwitchBot;

namespace BlokeBot.Features.Guessing.Commands;

public static class ModeratorPolicy
{
    public static bool IsModerator(TwitchChatMessage message)
    {
        if (string.Equals(message.Login, message.Channel, StringComparison.OrdinalIgnoreCase))
            return true;

        if (message.Tags.TryGetValue("mod", out var mod) && mod == "1")
            return true;

        if (!message.Tags.TryGetValue("badges", out var badges))
            return false;

        return badges
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(x =>
                x.StartsWith("broadcaster/", StringComparison.OrdinalIgnoreCase)
                || x.StartsWith("moderator/", StringComparison.OrdinalIgnoreCase)
            );
    }
}
