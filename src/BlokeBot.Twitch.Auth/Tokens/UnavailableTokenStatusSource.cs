using System.Collections.Immutable;
using BlokeBot.Functional;

namespace BlokeBot.Twitch.Auth;

public sealed class UnavailableTokenStatusSource : ITokenStatusSource
{
    public IO<TokenStatus, TokenStatusError> GetUserAccessTokenStatus(
        IEnumerable<string?> requiredScopes
    )
    {
        ArgumentNullException.ThrowIfNull(requiredScopes);
        var required = ImmutableArray.CreateRange(ScopeSet.NormalizeMany(requiredScopes));
        return IO<TokenStatus, TokenStatusError>.Create(cancellationToken =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(
                Result<TokenStatus, TokenStatusError>.Success(
                    new TokenStatus.Unavailable(
                        AccessTokenUnavailableReason.MissingRefreshToken,
                        required
                    )
                )
            );
        });
    }
}
