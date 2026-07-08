using System.Net;
using System.Text;
using BlokeBot.Twitch.Auth;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Twitch.Auth.Tests;

public sealed class TwitchTokenStatusServiceTests
{
    [Test]
    public async Task Status_is_unavailable_when_token_provider_is_missing()
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
    public async Task Status_is_invalid_when_twitch_rejects_token()
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
    public async Task Status_is_ready_for_valid_token_with_required_scopes()
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
    public async Task Status_reports_missing_scopes_for_valid_token()
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

    private static TwitchOAuthApiClient OAuthClient(string? validationJson) =>
        new(new StatusHttpClientFactory(validationJson));

    private sealed class ServiceProviderStub(ITwitchAccessTokenProvider? tokens) : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(ITwitchAccessTokenProvider) ? tokens : null;
    }

    private sealed class StaticTokenProvider(string accessToken) : ITwitchAccessTokenProvider
    {
        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken) =>
            Task.FromResult(accessToken);
    }

    private sealed class StatusHttpClientFactory(string? validationJson) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(new Handler(validationJson), disposeHandler: false);

        private sealed class Handler(string? validationJson) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken
            )
            {
                if (validationJson is null)
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));

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
