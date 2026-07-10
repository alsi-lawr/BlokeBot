using BlokeBot.Commands;

namespace BlokeBot.Twitch.Runtime;

/// <summary>
/// Observes Twitch chat messages before command dispatch.
/// </summary>
public interface ITwitchChatMessageObserver
{
    /// <summary>
    /// Handles a received chat message before the command dispatcher sees it.
    /// </summary>
    ValueTask MessageReceivedAsync(
        TwitchChatMessage message,
        CancellationToken cancellationToken
    );
}
