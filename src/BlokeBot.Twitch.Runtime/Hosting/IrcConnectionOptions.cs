using System.ComponentModel.DataAnnotations;

namespace BlokeBot.Twitch.Runtime;

/// <summary>
/// Configures a Twitch IRC connection.
/// </summary>
public sealed record IrcConnectionOptions
{
    /// <summary>
    /// Creates Twitch IRC connection options.
    /// </summary>
    public IrcConnectionOptions() { }

    /// <summary>
    /// Gets the IRC host name.
    /// </summary>
    [Required]
    public string Host { get; set; } = "irc.chat.twitch.tv";

    /// <summary>
    /// Gets the IRC port.
    /// </summary>
    [Range(1, 65535)]
    public int Port { get; set; } = 6667;

    /// <summary>
    /// Gets a value indicating whether TLS is used for the IRC connection.
    /// </summary>
    public bool UseTls { get; set; }
}
