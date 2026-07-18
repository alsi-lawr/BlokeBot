namespace BlokeBot.Twitch;

/// <summary>
/// Twitch OAuth scope names used by the bot runtime.
/// </summary>
public static class Scopes
{
    public const string ModeratorReadFollowers = "moderator:read:followers";
    public const string UserManageWhispers = "user:manage:whispers";
    public const string UserReadFollows = "user:read:follows";
    public const string UserReadModeratedChannels = "user:read:moderated_channels";
}
