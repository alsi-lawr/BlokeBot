namespace BlokeBot.Twitch.Runtime;

/// <summary>
/// Receives notifications when the bot lifecycle changes in a channel.
/// </summary>
public interface IBotChannelLifecycleNotifier
{
    /// <summary>
    /// Handles a channel startup confirmation.
    /// </summary>
    /// <param name="target">The channel runtime session that started.</param>
    /// <param name="cancellationToken">A token that cancels the notification.</param>
    /// <returns>A task that completes when the notification is handled.</returns>
    Task ChannelStartedAsync(BotChannelTarget target, CancellationToken cancellationToken);

    /// <summary>
    /// Handles a channel stop confirmation.
    /// </summary>
    /// <param name="target">The channel runtime session that stopped.</param>
    /// <param name="cancellationToken">A token that cancels the notification.</param>
    /// <returns>A task that completes when the notification is handled.</returns>
    Task ChannelStoppedAsync(BotChannelTarget target, CancellationToken cancellationToken);
}
