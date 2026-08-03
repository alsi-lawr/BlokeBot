using System.Net;
using System.Text;
using BlokeBot.Twitch.Auth;
using Shouldly;

namespace BlokeBot.Twitch.Runtime.Tests;

public sealed class TwitchEndpointRoutingTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ProductionAndLoopbackPolicies_RouteEveryProviderBoundary(bool useLoopback)
    {
        var policy = useLoopback
            ? new TwitchEndpointPolicy
            {
                OAuthOrigin = new Uri("http://127.0.0.1:5160/oauth2"),
                HelixOrigin = new Uri("http://127.0.0.1:5160/helix"),
            }
            : TwitchEndpointPolicy.Default;
        policy.Validate();

        var observedEndpoints = new List<Uri>();
        var factory = new RoutingHttpClientFactory(observedEndpoints);
        var context = new HelixRequestContext("client", "access");
        var routes = new[]
        {
            new EndpointRoute(
                "OAuthTransport",
                policy.OAuthOrigin,
                ["/oauth2/authorize", "/oauth2/token", "/oauth2/validate"],
                async () =>
                {
                    var client = new OAuthTransport(factory, policy);
                    observedEndpoints.Add(
                        client.CreateAuthorizationUri(
                            new AuthorizationUriRequest(
                                "client",
                                "https://localhost/callback",
                                OAuthAuthorizationScopeSet.Create(["chat:read"]),
                                "state",
                                AuthorizationVerificationPolicy.ReuseExistingAuthorization
                            )
                        )
                    );
                    _ = await client.ExchangeCodeAsync(
                        new AuthorizationCodeExchange(
                            "client",
                            "secret",
                            "https://localhost/callback",
                            "code"
                        ),
                        CancellationToken.None
                    );
                    _ = await client.ValidateTokenAsync("access", CancellationToken.None);
                }
            ),
            new EndpointRoute(
                "AppAccessTokenProvider",
                policy.OAuthOrigin,
                ["/oauth2/token"],
                async () =>
                {
                    using var provider = new AppAccessTokenProvider(factory, Identity(), policy);
                    _ = await provider.GetAccessTokenAsync(CancellationToken.None);
                }
            ),
            new EndpointRoute(
                "HelixClient",
                policy.HelixOrigin,
                ["/helix/users"],
                () =>
                    new HelixClient(factory, policy).GetCurrentUserAsync(
                        context,
                        CancellationToken.None
                    )
            ),
            new EndpointRoute(
                "ChatClient",
                policy.HelixOrigin,
                ["/helix/chat/messages"],
                () =>
                    new ChatClient(factory, policy).SendMessageAsync(
                        context,
                        "channel",
                        "bot",
                        "message",
                        CancellationToken.None
                    )
            ),
            new EndpointRoute(
                "ChatAnnouncementClient",
                policy.HelixOrigin,
                ["/helix/chat/announcements"],
                () =>
                    new ChatAnnouncementClient(factory, policy).SendAsync(
                        context,
                        "channel",
                        "moderator",
                        "message",
                        TwitchAnnouncementColor.Primary,
                        CancellationToken.None
                    )
            ),
            new EndpointRoute(
                "ChatPinClient",
                policy.HelixOrigin,
                ["/helix/chat/pins"],
                () =>
                    new ChatPinClient(factory, policy).PinAsync(
                        context,
                        "channel",
                        "moderator",
                        "message",
                        null,
                        CancellationToken.None
                    )
            ),
            new EndpointRoute(
                "WhisperClient",
                policy.HelixOrigin,
                ["/helix/whispers"],
                () =>
                    new WhisperClient(factory, policy).SendAsync(
                        context,
                        "sender",
                        "recipient",
                        "message",
                        CancellationToken.None
                    )
            ),
            new EndpointRoute(
                "EventSubClient",
                policy.HelixOrigin,
                ["/helix/eventsub/subscriptions"],
                () =>
                    new EventSubClient(
                        factory,
                        policy,
                        new EventSubWebhookOptions
                        {
                            CallbackUri = new Uri("https://bot.blokebot.com/eventsub/twitch"),
                            Secret = "eventsub-test-secret",
                        },
                        new StaticAppAccessTokenProvider(),
                        new ImmediateVerification()
                    ).CreateAsync(
                        "client",
                        new EventSubSubscriptionRequest(
                            "channel.chat.message",
                            "1",
                            new Dictionary<string, string>
                            {
                                ["broadcaster_user_id"] = "channel",
                                ["user_id"] = "bot",
                            }
                        ),
                        CancellationToken.None
                    )
            ),
        };

        foreach (var route in routes)
        {
            var start = observedEndpoints.Count;
            await route.RouteAsync();
            var endpoints = observedEndpoints[start..];

            endpoints.Count.ShouldBe(route.Paths.Length, route.Name);
            foreach (var (endpoint, path) in endpoints.Zip(route.Paths))
            {
                endpoint.Scheme.ShouldBe(route.Origin.Scheme, route.Name);
                endpoint.Authority.ShouldBe(route.Origin.Authority, route.Name);
                endpoint.AbsolutePath.ShouldBe(path, route.Name);
            }
        }
    }

    private static BotIdentity Identity() =>
        new BotIdentity
        {
            BotUsername = "bot",
            ClientId = "client",
            ClientSecret = "secret",
            RedirectUri = "https://localhost/callback",
            Scopes = OAuthAuthorizationScopeSet.Create(["chat:read"]),
            TokenCachePath = "tokens.json",
        };

    private sealed record EndpointRoute(
        string Name,
        Uri Origin,
        string[] Paths,
        Func<Task> RouteAsync
    );

    private sealed class StaticAppAccessTokenProvider : IAppAccessTokenProvider
    {
        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken) =>
            Task.FromResult("app-access-token");
    }

    private sealed class ImmediateVerification : IEventSubSubscriptionVerification
    {
        public Task WaitAsync(string subscriptionId, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public void Confirm(string subscriptionId) { }
    }

    private sealed class RoutingHttpClientFactory(List<Uri> observedEndpoints) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new HttpClient(new Handler(observedEndpoints), disposeHandler: false);

        private sealed class Handler(List<Uri> observedEndpoints) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken
            )
            {
                var endpoint = request.RequestUri!;
                observedEndpoints.Add(endpoint);
                return Task.FromResult(ResponseFor(endpoint.AbsolutePath));
            }

            private static HttpResponseMessage ResponseFor(string path) =>
                path switch
                {
                    var value when value.EndsWith("/token", StringComparison.Ordinal) => Json(
                        """{"access_token":"access","refresh_token":"refresh","expires_in":3600}"""
                    ),
                    var value when value.EndsWith("/validate", StringComparison.Ordinal) => Json(
                        """{"user_id":"1","login":"bot","scopes":[]}"""
                    ),
                    var value when value.EndsWith("/users", StringComparison.Ordinal) => Json(
                        """{"data":[]}"""
                    ),
                    var value when value.EndsWith("/chat/messages", StringComparison.Ordinal) =>
                        Json("""{"data":[{"message_id":"id","is_sent":true}]}"""),
                    var value
                        when value.EndsWith("/eventsub/subscriptions", StringComparison.Ordinal) =>
                        Json("""{"data":[{"id":"id"}]}"""),
                    _ => new HttpResponseMessage(HttpStatusCode.NoContent),
                };

            private static HttpResponseMessage Json(string json) =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                };
        }
    }
}
