namespace BlokeBot.Twitch.Runtime;

/// <summary>
/// Provides the Twitch chat channels that the bot should connect to.
/// </summary>
public interface IBotChannelProvider
{
    /// <summary>
    /// Gets the channel runtime sessions that should be connected.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the channel lookup.</param>
    /// <returns>The channel runtime sessions to connect.</returns>
    ValueTask<IReadOnlyList<BotChannelTarget>> GetChannelsAsync(
        CancellationToken cancellationToken
    );
}
