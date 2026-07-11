using System.Net;
using System.Text;
using BlokeBot.Features.AccessLists;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Tests;

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

        profiles.ShouldBe(
        [
            new AccessListEntryProfile("Viewer", null),
            new AccessListEntryProfile("MODERATOR", null),
        ]
        );
    }

    [Test]
    public async Task TwitchEnrichment_ResolvingLogins_ReturnsAvailableProfileImages()
    {
        var http = new ProfileHttpClientFactory();
        var identity = TwitchBotIdentity.FromOptions(
            new TwitchBotIdentityOptions
            {
                BotUsername = "bot",
                ClientId = "client-id",
                ClientSecret = "client-secret",
                RedirectUri = "https://localhost/oauth/callback",
                Scopes = ["chat:read"],
                TokenCachePath = "tokens.json",
            }
        );
        var resolver = new AccessListProfileResolver(
            new TwitchAccessListProfileEnrichmentPolicy(
                new TwitchAppAccessTokenProvider(http, identity),
                new TwitchHelixApiClient(http),
                identity
            )
        );

        var profiles = await resolver.ResolveAsync(
            [" Viewer ", "missing", "BLANK"],
            CancellationToken.None
        );

        profiles.ShouldBe(
        [
            new AccessListEntryProfile("Viewer", "https://cdn.example/viewer.png"),
            new AccessListEntryProfile("missing", null),
            new AccessListEntryProfile("BLANK", null),
        ]
        );
        http.TokenRequestCount.ShouldBe(1);
        http.UserRequestCount.ShouldBe(1);
        http.UserRequestClientId.ShouldBe("client-id");
        http.UserRequestAccessToken.ShouldBe("app-token");
    }

    private sealed class ProfileHttpClientFactory : IHttpClientFactory
    {
        private readonly Handler handler = new();

        public int TokenRequestCount => handler.TokenRequestCount;

        public int UserRequestCount => handler.UserRequestCount;

        public string? UserRequestAccessToken => handler.UserRequestAccessToken;

        public string? UserRequestClientId => handler.UserRequestClientId;

        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);

        private sealed class Handler : HttpMessageHandler
        {
            public int TokenRequestCount { get; private set; }

            public int UserRequestCount { get; private set; }

            public string? UserRequestAccessToken { get; private set; }

            public string? UserRequestClientId { get; private set; }

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
                TokenRequestCount++;
                return JsonResponse(
                    """
                    {"access_token":"app-token","expires_in":3600}
                    """
                );
            }

            private HttpResponseMessage UserResponse(HttpRequestMessage request)
            {
                UserRequestCount++;
                UserRequestAccessToken = request.Headers.Authorization?.Parameter;
                UserRequestClientId = request.Headers.GetValues("Client-Id").Single();
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
