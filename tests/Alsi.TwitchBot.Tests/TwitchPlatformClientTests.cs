using System.Net;
using System.Text;
using Alsi.TwitchBot;
using Shouldly;
using TUnit.Core;

namespace Alsi.TwitchBot.Tests;

public sealed class TwitchPlatformClientTests
{
    [Test]
    public void OAuth_client_builds_authorization_uri_with_normalized_scopes()
    {
        var client = new TwitchOAuthApiClient(new ScriptedHttpClientFactory());

        var uri = client.CreateAuthorizationUri(
            new TwitchAuthorizationUriRequest(
                "client",
                "https://localhost/callback",
                [" channel:bot ", "BITS:READ", "bits:read"],
                "state value"
            )
        );

        uri.AbsoluteUri.ShouldContain("client_id=client");
        uri.AbsoluteUri.ShouldContain("redirect_uri=https%3A%2F%2Flocalhost%2Fcallback");
        uri.AbsoluteUri.ShouldContain("scope=bits%3Aread%20channel%3Abot");
        uri.AbsoluteUri.ShouldContain("state=state%20value");
        uri.AbsoluteUri.ShouldContain("force_verify=true");
    }

    [Test]
    public async Task OAuth_client_exchanges_code_and_validates_token_payload()
    {
        var factory = new ScriptedHttpClientFactory();
        factory.Respond(request =>
        {
            request.RequestUri!.AbsolutePath.ShouldBe("/oauth2/token");
            var form = ReadContent(request);
            form.ShouldContain("grant_type=authorization_code");
            form.ShouldContain("client_id=client");
            form.ShouldContain("client_secret=secret");
            form.ShouldContain("code=code");
            return JsonResponse(
                """
                {"access_token":"access","refresh_token":"refresh","expires_in":3600}
                """
            );
        });
        factory.Respond(request =>
        {
            request.RequestUri!.AbsolutePath.ShouldBe("/oauth2/validate");
            request.Headers.Authorization!.Scheme.ShouldBe("OAuth");
            request.Headers.Authorization.Parameter.ShouldBe("access");
            return JsonResponse(
                """
                {"user_id":"123","login":"Streamer","scopes":["BITS:READ"," channel:bot "]}
                """
            );
        });
        var client = new TwitchOAuthApiClient(factory);

        var token = await client.ExchangeCodeAsync(
            new TwitchAuthorizationCodeExchange(
                "client",
                "secret",
                "https://localhost/callback",
                "code"
            ),
            CancellationToken.None
        );
        var validation = await client.ValidateTokenAsync(token.AccessToken, CancellationToken.None);

        token.AccessToken.ShouldBe("access");
        token.RefreshToken.ShouldBe("refresh");
        token.ExpiresIn.ShouldBe(3600);
        validation.ShouldNotBeNull();
        validation.UserId.ShouldBe("123");
        validation.Login.ShouldBe("streamer");
        validation.Scopes.ShouldBe(["bits:read", "channel:bot"], ignoreOrder: true);
    }

    [Test]
    public async Task Helix_client_loads_paginated_moderated_channels()
    {
        var factory = new ScriptedHttpClientFactory();
        factory.Respond(request =>
        {
            request.Headers.Authorization!.Scheme.ShouldBe("Bearer");
            request.Headers.Authorization.Parameter.ShouldBe("token");
            request.Headers.GetValues("Client-Id").Single().ShouldBe("client");
            request.RequestUri!.Query.ShouldContain("first=100");
            request.RequestUri.Query.ShouldContain("user_id=bot-id");
            return JsonResponse(
                """
                {
                  "data": [
                    {"broadcaster_id":"1","broadcaster_login":"one","broadcaster_name":"One"}
                  ],
                  "pagination": {"cursor":"next"}
                }
                """
            );
        });
        factory.Respond(request =>
        {
            request.RequestUri!.Query.ShouldContain("after=next");
            return JsonResponse(
                """
                {
                  "data": [
                    {"broadcaster_id":"2","broadcaster_login":"two","broadcaster_name":"Two"}
                  ],
                  "pagination": {}
                }
                """
            );
        });
        var client = new TwitchHelixApiClient(factory);

        var channels = await client.GetModeratedChannelsAsync(
            new TwitchHelixRequestContext("client", "token"),
            "bot-id",
            CancellationToken.None
        );

        channels.Select(channel => channel.BroadcasterLogin).ShouldBe(["one", "two"]);
    }

    private static string ReadContent(HttpRequestMessage request) =>
        request.Content?.ReadAsStringAsync(CancellationToken.None).GetAwaiter().GetResult()
        ?? string.Empty;

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    private sealed class ScriptedHttpClientFactory : IHttpClientFactory
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> responses = new();

        public void Respond(Func<HttpRequestMessage, HttpResponseMessage> response) =>
            responses.Enqueue(response);

        public HttpClient CreateClient(string name) =>
            new(new Handler(responses), disposeHandler: false);

        private sealed class Handler(
            Queue<Func<HttpRequestMessage, HttpResponseMessage>> responses
        ) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken
            )
            {
                responses.Count.ShouldBeGreaterThan(0);
                return Task.FromResult(responses.Dequeue()(request));
            }
        }
    }
}
