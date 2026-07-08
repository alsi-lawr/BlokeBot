namespace BlokeBot.Twitch.Runtime;

/// <summary>
/// Provides the Twitch chat channels that the bot should connect to.
/// </summary>
public interface ITwitchBotChannelProvider
{
    /// <summary>
    /// Gets the channel logins that should be connected, without leading hash characters.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the channel lookup.</param>
    /// <returns>The channel logins to connect.</returns>
    ValueTask<IReadOnlyList<string>> GetChannelsAsync(CancellationToken cancellationToken);
}
