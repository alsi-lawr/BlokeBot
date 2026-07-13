namespace BlokeBot.Twitch.Runtime;

/// <summary>
/// Describes the latest observed Twitch bot runtime status.
/// </summary>
/// <param name="IsAuthorized">Whether the bot has a usable chat token.</param>
/// <param name="IsConnected">Whether the bot runtime is connected to Twitch chat.</param>
/// <param name="ConnectedChannels">The channel logins currently connected by the runtime.</param>
public sealed record BotRuntimeStatus(
    bool IsAuthorized,
    bool IsConnected,
    IReadOnlyList<string> ConnectedChannels
);
