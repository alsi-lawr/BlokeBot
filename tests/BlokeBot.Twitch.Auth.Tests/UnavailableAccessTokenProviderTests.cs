using BlokeBot.Functional;
using BlokeBot.Twitch.Auth;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Twitch.Auth.Tests;

public sealed class UnavailableAccessTokenProviderTests
{
    [Test]
    public async Task GetAccessToken_Executing_ReturnsMissingRefreshToken()
    {
        var provider = new UnavailableAccessTokenProvider();

        var result = await provider.GetAccessToken().ExecuteAsync(CancellationToken.None);

        var reason = result.Match(
            _ => throw new InvalidOperationException("Expected an unavailable access token."),
            unavailable => unavailable
        );
        reason.ShouldBe(AccessTokenUnavailableReason.MissingRefreshToken);
    }
}
