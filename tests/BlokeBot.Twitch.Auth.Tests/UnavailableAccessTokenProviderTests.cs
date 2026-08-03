using Shouldly;

namespace BlokeBot.Twitch.Auth.Tests;

public sealed class UnavailableAccessTokenProviderTests
{
    [Test]
    public async Task GetAccessToken_Executing_ReturnsMissingRefreshToken()
    {
        var provider = new UnavailableAccessTokenProvider();

        var result = await provider.GetAccessToken().ExecuteAsync(CancellationToken.None);

        var reason = result.Match(
            static _ =>
                throw new InvalidOperationException("Expected an unavailable access token."),
            static unavailable => unavailable
        );
        reason.ShouldBe(AccessTokenUnavailableReason.MissingRefreshToken);
    }
}
