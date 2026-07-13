using System.Net;
using System.Text;
using BlokeBot.Twitch.Auth;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Twitch.Auth.Tests;

public sealed class TwitchTokenStatusServiceTests
{
    [Test]
    public async Task MissingTokenProvider_LoadingStatus_ReturnsUnavailableWithRequiredScopes()
    {
        var service = new TwitchTokenStatusService(
            new ServiceProviderStub(null),
            OAuthClient("""{"user_id":"123","login":"bot","scopes":["chat:read"]}""")
        );

        var status = await service.GetUserAccessTokenStatusAsync(
            ["chat:read"],
            CancellationToken.None
        );

        status.State.ShouldBe(TwitchTokenStatusState.Unavailable);
        status.AccessToken.ShouldBeNull();
        status.RequiredScopes.ShouldBe(["chat:read"]);
        status.MissingScopes.ShouldBe(["chat:read"]);
    }

    [Test]
    public async Task UnavailableAccessToken_LoadingStatus_ReturnsUnavailable()
    {
        var service = new TwitchTokenStatusService(
            new ServiceProviderStub(new UnavailableTokenProvider()),
            OAuthClient("""{"user_id":"123","login":"bot","scopes":["chat:read"]}""")
        );

        var status = await service.GetUserAccessTokenStatusAsync(
            ["chat:read"],
            CancellationToken.None
        );

        status.State.ShouldBe(TwitchTokenStatusState.Unavailable);
        status.AccessToken.ShouldBeNull();
        status.MissingScopes.ShouldBe(["chat:read"]);
    }

    [Test]
    public async Task RejectedAccessToken_LoadingStatus_ReturnsInvalidWithToken()
    {
        var service = new TwitchTokenStatusService(
            new ServiceProviderStub(new StaticTokenProvider("saved-token")),
            OAuthClient(null)
        );

        var status = await service.GetUserAccessTokenStatusAsync(
            ["chat:read"],
            CancellationToken.None
        );

        status.State.ShouldBe(TwitchTokenStatusState.Invalid);
        status.AccessToken.ShouldBe("saved-token");
        status.Validation.ShouldBeNull();
        status.MissingScopes.ShouldBe(["chat:read"]);
    }

    [Test]
    public async Task ValidTokenWithRequiredScopes_LoadingStatus_ReturnsReady()
    {
        var service = new TwitchTokenStatusService(
            new ServiceProviderStub(new StaticTokenProvider("saved-token")),
            OAuthClient(
                """{"user_id":"123","login":"BotAccount","scopes":["chat:edit","chat:read"]}"""
            )
        );

        var status = await service.GetUserAccessTokenStatusAsync(
            ["chat:read", "chat:edit"],
            CancellationToken.None
        );

        status.State.ShouldBe(TwitchTokenStatusState.Ready);
        status.AccessToken.ShouldBe("saved-token");
        status.Validation.ShouldNotBeNull();
        status.Validation.Login.ShouldBe("botaccount");
        status.GrantedScopes.ShouldBe(["chat:edit", "chat:read"]);
        status.MissingScopes.ShouldBeEmpty();
    }

    [Test]
    public async Task ValidTokenMissingScope_LoadingStatus_ReturnsMissingScopes()
    {
        var service = new TwitchTokenStatusService(
            new ServiceProviderStub(new StaticTokenProvider("saved-token")),
            OAuthClient("""{"user_id":"123","login":"bot","scopes":["chat:read"]}""")
        );

        var status = await service.GetUserAccessTokenStatusAsync(
            ["chat:read", "chat:edit"],
            CancellationToken.None
        );

        status.State.ShouldBe(TwitchTokenStatusState.MissingScopes);
        status.AccessToken.ShouldBe("saved-token");
        status.GrantedScopes.ShouldBe(["chat:read"]);
        status.MissingScopes.ShouldBe(["chat:edit"]);
    }

    private static TwitchOAuthApiClient OAuthClient(string? validationJson)
    {
        return new(new StatusHttpClientFactory(validationJson));
    }

    private sealed class ServiceProviderStub(ITwitchAccessTokenProvider? tokens) : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            return serviceType == typeof(ITwitchAccessTokenProvider) ? tokens : null;
        }
    }

    private sealed class StaticTokenProvider(string accessToken) : ITwitchAccessTokenProvider
    {
        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(accessToken);
        }
    }

    private sealed class UnavailableTokenProvider : ITwitchAccessTokenProvider
    {
        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
        {
            throw new TwitchAccessTokenUnavailableException(
                TwitchAccessTokenUnavailableReason.MissingRefreshToken,
                TwitchAccessTokenUnavailableException.MissingRefreshTokenMessage
            );
        }
    }

    private sealed class StatusHttpClientFactory(string? validationJson) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new(new Handler(validationJson), disposeHandler: false);
        }

        private sealed class Handler(string? validationJson) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken
            )
            {
                if (validationJson is null)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
                }

                return Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            validationJson,
                            Encoding.UTF8,
                            "application/json"
                        ),
                    }
                );
            }
        }
    }
}
