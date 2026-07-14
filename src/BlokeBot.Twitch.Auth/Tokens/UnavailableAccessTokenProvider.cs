using BlokeBot.Functional;

namespace BlokeBot.Twitch.Auth;

/// <summary>
/// Reports that no Twitch bot token capability is configured.
/// </summary>
public sealed class UnavailableAccessTokenProvider : IAccessTokenProvider
{
    public IO<string, AccessTokenUnavailableReason> GetAccessToken()
    {
        return IO<string, AccessTokenUnavailableReason>.Create(cancellationToken =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(
                Result<string, AccessTokenUnavailableReason>.Error(
                    AccessTokenUnavailableReason.MissingRefreshToken
                )
            );
        });
    }
}
