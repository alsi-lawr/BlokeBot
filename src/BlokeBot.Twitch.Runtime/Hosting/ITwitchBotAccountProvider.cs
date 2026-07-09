namespace BlokeBot.Twitch.Runtime;

/// <summary>
/// Provides the bot account that should act in a specific Twitch channel.
/// </summary>
public interface ITwitchBotAccountProvider
{
    /// <summary>
    /// Gets the bot account that should send messages for the channel login.
    /// </summary>
    /// <param name="channelLogin">The channel login, without a leading hash.</param>
    /// <param name="cancellationToken">A token that cancels the account lookup.</param>
    /// <returns>The active bot account for the channel.</returns>
    ValueTask<TwitchBotAccount> GetBotAccountAsync(
        string channelLogin,
        CancellationToken cancellationToken
    );
}

public sealed record TwitchBotAccount(string Login, string AccessToken);
