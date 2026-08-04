using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.CustomCommands;

internal static class CustomCommandAccessPolicy
{
    public static string Describe(bool allowEveryone, bool allowModerators, int selectedUserCount)
    {
        if (allowEveryone)
        {
            return "Everyone";
        }

        var grants = new List<string>();
        if (allowModerators)
        {
            grants.Add("Moderators");
        }
        if (selectedUserCount > 0)
        {
            grants.Add(
                selectedUserCount == 1
                    ? "1 selected person"
                    : $"{selectedUserCount} selected people"
            );
        }
        return grants.Count == 0 ? "Streamer only" : string.Join(" + ", grants);
    }

    public static bool Allows(string channelLogin, CustomCommand command, ChatMessage message) =>
        Allows(
            channelLogin,
            command.AllowEveryone,
            command.AllowModerators,
            command.AllowedUsers.Select(static user => user.TwitchUserId),
            message
        );

    public static bool Allows(
        string channelLogin,
        bool allowEveryone,
        bool allowModerators,
        IEnumerable<string> allowedTwitchUserIds,
        ChatMessage message
    ) =>
        string.Equals(message.Login, channelLogin, StringComparison.OrdinalIgnoreCase)
        || allowEveryone
        || (allowModerators && ChatModeratorPolicy.IsModerator(message))
        || (
            message.Tags.TryGetValue("user-id", out var twitchUserId)
            && twitchUserId.Length > 0
            && allowedTwitchUserIds.Contains(twitchUserId, StringComparer.Ordinal)
        );
}
