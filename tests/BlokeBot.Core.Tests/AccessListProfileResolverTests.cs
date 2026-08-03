using System.Net;
using System.Text;
using BlokeBot.Core.Features.AccessLists;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed class AccessListProfileResolverTests
{
    [Test]
    public async Task EnrichmentDisabled_ResolvingLogins_ReturnsProfilesWithoutImages()
    {
        var resolver = new AccessListProfileResolver(
            new DisabledAccessListProfileEnrichmentPolicy()
        );

        var profiles = await resolver.ResolveAsync(
            [" Viewer ", " ", "MODERATOR"],
            CancellationToken.None
        );

        profiles.ShouldBe([
            new AccessListEntryProfile("Viewer", null),
            new AccessListEntryProfile("MODERATOR", null),
        ]);
    }

    [Test]
    public async Task HelixEnrichment_ResolvingLogins_ReturnsAvailableProfileImages()
    {
        var http = new ProfileHttpClientFactory();
        var identity = BotIdentity.FromOptions(
            new BotIdentityOptions
            {
                BotUsername = "bot",
                ClientId = "client-id",
                ClientSecret = "client-secret",
                RedirectUri = "https://localhost/oauth/callback",
                Scopes = ["chat:read"],
                TokenCachePath = "tokens.json",
            }
        );
        using var appTokens = new AppAccessTokenProvider(
            http,
            identity,
            global::BlokeBot.Twitch.TwitchEndpointPolicy.Default
        );
        var resolver = new AccessListProfileResolver(
            new HelixAccessListProfileEnrichmentPolicy(
                appTokens,
                new HelixClient(http, global::BlokeBot.Twitch.TwitchEndpointPolicy.Default),
                identity
            )
        );

        var profiles = await resolver.ResolveAsync(
            [" Viewer ", "missing", "BLANK"],
            CancellationToken.None
        );

        profiles.ShouldBe([
            new AccessListEntryProfile("Viewer", "https://cdn.example/viewer.png"),
            new AccessListEntryProfile("missing", null),
            new AccessListEntryProfile("BLANK", null),
        ]);
        http.TokenRequestCount.ShouldBe(1);
        http.UserRequestCount.ShouldBe(1);
        http.UserRequestClientId.ShouldBe("client-id");
        http.UserRequestAccessToken.ShouldBe("app-token");
    }

    private sealed class ProfileHttpClientFactory : IHttpClientFactory
    {
        internal int TokenRequestCountValue { get; set; }
        internal int UserRequestCountValue { get; set; }
        internal string? UserRequestAccessTokenValue { get; set; }
        internal string? UserRequestClientIdValue { get; set; }

        public int TokenRequestCount => TokenRequestCountValue;
        public int UserRequestCount => UserRequestCountValue;
        public string? UserRequestAccessToken => UserRequestAccessTokenValue;
        public string? UserRequestClientId => UserRequestClientIdValue;

        public HttpClient CreateClient(string name) => new(new Handler(this));

        private sealed class Handler(ProfileHttpClientFactory owner) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken
            ) =>
                Task.FromResult(
                    request.RequestUri?.AbsolutePath switch
                    {
                        "/oauth2/token" => TokenResponse(),
                        "/helix/users" => UserResponse(request),
                        _ => new HttpResponseMessage(HttpStatusCode.NotFound),
                    }
                );

            private HttpResponseMessage TokenResponse()
            {
                owner.TokenRequestCountValue++;
                return JsonResponse(
                    """
                    {"access_token":"app-token","expires_in":3600}
                    """
                );
            }

            private HttpResponseMessage UserResponse(HttpRequestMessage request)
            {
                owner.UserRequestCountValue++;
                owner.UserRequestAccessTokenValue = request.Headers.Authorization?.Parameter;
                owner.UserRequestClientIdValue = request.Headers.GetValues("Client-Id").Single();
                return JsonResponse(
                    """
                    {"data":[{"id":"1","login":"viewer","display_name":"Viewer","profile_image_url":"https://cdn.example/viewer.png"},{"id":"2","login":"blank","display_name":"Blank","profile_image_url":" "}]}
                    """
                );
            }

            private static HttpResponseMessage JsonResponse(string json) =>
                new(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                };
        }
    }
}
