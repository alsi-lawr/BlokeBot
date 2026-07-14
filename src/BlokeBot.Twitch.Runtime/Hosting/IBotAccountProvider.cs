using BlokeBot.Functional;

namespace BlokeBot.Twitch.Runtime;

/// <summary>
/// Provides the bot account that should act in a specific Twitch channel.
/// </summary>
public interface IBotAccountProvider
{
    /// <summary>
    /// Gets the bot account that should send messages for the channel login.
    /// </summary>
    /// <param name="channelLogin">The channel login, without a leading hash.</param>
    /// <returns>A deferred active bot-account lookup.</returns>
    IO<BotAccount, AccessTokenUnavailableReason> GetBotAccount(string channelLogin);
}

public sealed record BotAccount(string Login, string AccessToken);
