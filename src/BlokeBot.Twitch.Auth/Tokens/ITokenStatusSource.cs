using BlokeBot.Functional;

namespace BlokeBot.Twitch.Auth;

public interface ITokenStatusSource
{
    IO<TokenStatus, TokenStatusError> GetUserAccessTokenStatus(IEnumerable<string?> requiredScopes);
}
