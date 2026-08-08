namespace BlokeBot.Twitch;

/// <summary>
/// Twitch OAuth scope names used by the bot runtime.
/// </summary>
public static class Scopes
{
    public const string ModeratorReadFollowers = "moderator:read:followers";
    public const string ModeratorReadChatters = "moderator:read:chatters";
    public const string ModeratorManageAnnouncements = "moderator:manage:announcements";
    public const string ModeratorManageChatMessages = "moderator:manage:chat_messages";
    public const string UserManageWhispers = "user:manage:whispers";
    public const string UserReadFollows = "user:read:follows";
    public const string UserReadModeratedChannels = "user:read:moderated_channels";
    public const string ModeratorReadShoutouts = "moderator:read:shoutouts";
    public const string ModeratorManageShoutouts = "moderator:manage:shoutouts";
}
