using System.ComponentModel.DataAnnotations;

namespace BlokeBot.Twitch.Runtime;

/// <summary>
/// Configures a Twitch bot runtime.
/// </summary>
public sealed record TwitchBotOptions
{
    /// <summary>
    /// Creates Twitch bot options.
    /// </summary>
    public TwitchBotOptions() { }

    /// <summary>
    /// Gets the chat runtime used by the bot.
    /// </summary>
    public TwitchBotRuntime Runtime { get; set; } = TwitchBotRuntime.Irc;

    /// <summary>
    /// Gets the minimum number of seconds between outbound chat messages.
    /// </summary>
    public int ChatMessageSendIntervalSeconds { get; set; } = 1;

    /// <summary>
    /// Gets the number of seconds before the same outbound message can be repeated in the same channel.
    /// </summary>
    public int DuplicateChatMessageCooldownSeconds { get; set; } = 30;

    /// <summary>
    /// Gets the maximum number of characters in each outbound chat message.
    /// </summary>
    public int MaxChatMessageLength { get; set; } = 500;

    /// <summary>
    /// Gets the chat message sent when the bot starts in a channel.
    /// </summary>
    public string StartupMessage { get; set; } = string.Empty;

    /// <summary>
    /// Gets the Twitch IRC connection settings.
    /// </summary>
    [Required]
    public TwitchBotConnectionOptions Connection { get; set; } = new();

    /// <summary>
    /// Gets the Twitch identity and OAuth settings.
    /// </summary>
    [Required]
    public TwitchBotIdentityOptions Identity { get; set; } = new();
}
