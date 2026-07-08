namespace BlokeBot.Twitch.Runtime;

/// <summary>
/// Receives notifications when the bot lifecycle changes in a channel.
/// </summary>
public interface ITwitchBotChannelLifecycleNotifier
{
    /// <summary>
    /// Handles a channel startup confirmation.
    /// </summary>
    /// <param name="channel">The channel login that started.</param>
    /// <param name="cancellationToken">A token that cancels the notification.</param>
    /// <returns>A task that completes when the notification is handled.</returns>
    Task ChannelStartedAsync(string channel, CancellationToken cancellationToken);

    /// <summary>
    /// Handles a channel stop confirmation.
    /// </summary>
    /// <param name="channel">The channel login that stopped.</param>
    /// <param name="cancellationToken">A token that cancels the notification.</param>
    /// <returns>A task that completes when the notification is handled.</returns>
    Task ChannelStoppedAsync(string channel, CancellationToken cancellationToken);
}
