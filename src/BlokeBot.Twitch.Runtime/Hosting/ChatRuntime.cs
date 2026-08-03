namespace BlokeBot.Twitch.Runtime;

/// <summary>
/// Selects the Twitch chat runtime used by the bot.
/// </summary>
public enum ChatRuntime
{
    /// <summary>
    /// Uses Twitch IRC to receive and send chat messages.
    /// </summary>
    Irc,

    /// <summary>
    /// Uses Twitch EventSub webhooks to receive chat messages and Helix to send replies.
    /// </summary>
    EventSub,
}
