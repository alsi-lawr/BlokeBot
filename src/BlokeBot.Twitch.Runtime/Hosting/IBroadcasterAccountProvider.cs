using BlokeBot.Functional;

namespace BlokeBot.Twitch.Runtime;

public interface IBroadcasterAccountProvider
{
    IO<BotAccount, AccessTokenUnavailableReason> GetBroadcasterAccount(string channelLogin);
}
