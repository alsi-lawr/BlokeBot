namespace BlokeBot.Twitch.Runtime;

/// <summary>
/// Selects the Twitch chat runtime used by the bot.
/// </summary>
public enum TwitchBotRuntime
{
    /// <summary>
    /// Uses Twitch IRC to receive and send chat messages.
    /// </summary>
    Irc,

    /// <summary>
    /// Uses EventSub WebSocket to receive chat messages and Helix to send replies.
    /// </summary>
    EventSub,
}
