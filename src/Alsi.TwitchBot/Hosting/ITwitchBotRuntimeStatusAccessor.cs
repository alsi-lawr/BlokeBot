namespace Alsi.TwitchBot;

/// <summary>
/// Provides the current Twitch bot runtime status for application UI.
/// </summary>
public interface ITwitchBotRuntimeStatusAccessor
{
    /// <summary>
    /// Occurs when the observed bot runtime status changes.
    /// </summary>
    event Action? Changed;

    /// <summary>
    /// Gets the latest observed bot runtime status.
    /// </summary>
    TwitchBotRuntimeStatus Current { get; }
}
