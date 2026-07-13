using System.Collections.ObjectModel;

namespace BlokeBot.Commands;

/// <summary>
/// Describes a Twitch chat message received by the bot.
/// </summary>
public sealed record ChatMessage
{
    /// <summary>
    /// Creates a chat message.
    /// </summary>
    /// <param name="login">The login that sent the message.</param>
    /// <param name="channel">The channel where the message was sent.</param>
    /// <param name="text">The message text.</param>
    /// <param name="rawLine">The raw source payload.</param>
    /// <param name="tags">The raw tag values keyed by tag name.</param>
    public ChatMessage(
        string login,
        string channel,
        string text,
        string rawLine,
        IReadOnlyDictionary<string, string> tags
    )
    {
        Login = login;
        Channel = channel;
        Text = text;
        RawLine = rawLine;
        Tags = new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(tags, StringComparer.Ordinal)
        );
    }

    /// <summary>
    /// Gets the login that sent the message.
    /// </summary>
    public string Login { get; }

    /// <summary>
    /// Gets the channel where the message was sent.
    /// </summary>
    public string Channel { get; }

    /// <summary>
    /// Gets the message text.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Gets the raw source payload.
    /// </summary>
    public string RawLine { get; }

    /// <summary>
    /// Gets the raw tag values keyed by tag name.
    /// </summary>
    public IReadOnlyDictionary<string, string> Tags { get; }
}
