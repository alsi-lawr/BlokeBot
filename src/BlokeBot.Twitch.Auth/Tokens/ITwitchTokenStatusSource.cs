using BlokeBot.Functional;

namespace BlokeBot.Twitch.Auth;

public interface ITwitchTokenStatusSource
{
    IO<TwitchTokenStatus, TwitchTokenStatusError> GetUserAccessTokenStatus(
        IEnumerable<string?> requiredScopes
    );
}
