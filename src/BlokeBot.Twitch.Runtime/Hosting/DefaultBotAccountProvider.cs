using BlokeBot.Functional;

namespace BlokeBot.Twitch.Runtime;

internal sealed class DefaultBotAccountProvider(BotSettings settings, IAccessTokenProvider tokens)
    : IBotAccountProvider
{
    public IO<BotAccount, AccessTokenUnavailableReason> GetBotAccount(string channelLogin)
    {
        return tokens
            .GetAccessToken()
            .Map(accessToken => new BotAccount(
                Login.Normalize(settings.Identity.BotUsername),
                accessToken
            ));
    }
}
