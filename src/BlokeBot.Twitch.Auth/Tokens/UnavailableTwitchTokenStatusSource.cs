using System.Collections.Immutable;
using BlokeBot.Functional;

namespace BlokeBot.Twitch.Auth;

public sealed class UnavailableTwitchTokenStatusSource : ITwitchTokenStatusSource
{
    public IO<TwitchTokenStatus, TwitchTokenStatusError> GetUserAccessTokenStatus(
        IEnumerable<string?> requiredScopes
    )
    {
        ArgumentNullException.ThrowIfNull(requiredScopes);
        var required = ImmutableArray.CreateRange(TwitchScopeSet.NormalizeMany(requiredScopes));
        return IO<TwitchTokenStatus, TwitchTokenStatusError>.Create(cancellationToken =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(
                Result<TwitchTokenStatus, TwitchTokenStatusError>.Success(
                    new TwitchTokenStatus.Unavailable(
                        TwitchAccessTokenUnavailableReason.MissingRefreshToken,
                        required
                    )
                )
            );
        });
    }
}
