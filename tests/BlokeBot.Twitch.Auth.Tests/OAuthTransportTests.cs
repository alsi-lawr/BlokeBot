using System.Net;
using System.Text;
using System.Text.Json;
using BlokeBot.Twitch.Auth;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Twitch.Auth.Tests;

public sealed class OAuthTransportTests
{
    [Test]
    public void DuplicateNoisyScopes_CreatingAuthorizationUri_NormalizesAndEncodesRequest()
    {
        var client = new OAuthTransport(new ScriptedHttpClientFactory());
        string[] requestedScopes = [" channel:bot ", "BITS:READ", "bits:read"];
        var scopes = OAuthAuthorizationScopeSet.Create(requestedScopes);
        requestedScopes[0] = "user:write:chat";

        var uri = client.CreateAuthorizationUri(
            new AuthorizationUriRequest(
                "client",
                "https://localhost/callback",
                scopes,
                "state value",
                AuthorizationVerificationPolicy.ForceAccountVerification
            )
        );

        uri.AbsoluteUri.ShouldContain("client_id=client");
        uri.AbsoluteUri.ShouldContain("redirect_uri=https%3A%2F%2Flocalhost%2Fcallback");
        uri.AbsoluteUri.ShouldContain("scope=bits%3Aread%20channel%3Abot");
        uri.AbsoluteUri.ShouldContain("state=state%20value");
        uri.AbsoluteUri.ShouldContain("force_verify=true");
    }

    [Test]
    public void SingleExplicitScope_CreatingAuthorizationUri_SerializesExactSelection()
    {
        var client = new OAuthTransport(new ScriptedHttpClientFactory());

        var uri = client.CreateAuthorizationUri(
            new AuthorizationUriRequest(
                "client",
                "https://localhost/callback",
                OAuthAuthorizationScopeSet.Create(["chat:read"]),
                "state",
                AuthorizationVerificationPolicy.ReuseExistingAuthorization
            )
        );

        uri.AbsoluteUri.ShouldContain("scope=chat%3Aread");
        uri.AbsoluteUri.ShouldNotContain("force_verify");
        uri.AbsoluteUri.ShouldContain("state=state");
    }

    [Test]
    public void InvalidAuthorizationScopeValues_CreatingScopeSet_RejectsInvalidElements()
    {
        Should.Throw<ArgumentNullException>(() => OAuthAuthorizationScopeSet.Create(null!));
        Should.Throw<ArgumentException>(() => OAuthAuthorizationScopeSet.Create([]));
        Should.Throw<ArgumentException>(() => OAuthAuthorizationScopeSet.Create([null!]));
        Should.Throw<ArgumentException>(() => OAuthAuthorizationScopeSet.Create([" "]));
        Should.Throw<ArgumentException>(() => OAuthAuthorizationScopeSet.Create(["chat read"]));
    }

    [Test]
    public async Task AuthorizationCodeAndTokenPayload_ExchangingThenValidating_MapsRequestsAndResponses()
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
        var client = new OAuthTransport(factory);

        var token = await client.ExchangeCodeAsync(
            new AuthorizationCodeExchange("client", "secret", "https://localhost/callback", "code"),
            CancellationToken.None
        );
        var validation = (
            await client.ValidateTokenAsync(token.AccessToken, CancellationToken.None)
        )
            .ShouldBeOfType<TokenValidationOutcome.Validated>()
            .Validation;

        token.AccessToken.ShouldBe("access");
        token.RefreshToken.ShouldBe("refresh");
        token.ExpiresIn.ShouldBe(3600);
        validation.UserId.ShouldBe("123");
        validation.Login.ShouldBe("streamer");
        validation.Scopes.ShouldBe(["bits:read", "channel:bot"], ignoreOrder: true);
    }

    [Test]
    public async Task InvalidToken_Validating_ReturnsTypedRejection()
    {
        var factory = new ScriptedHttpClientFactory();
        factory.Respond(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var client = new OAuthTransport(factory);

        var outcome = await client.ValidateTokenAsync("invalid", CancellationToken.None);

        outcome.ShouldBeOfType<TokenValidationOutcome.NotValidated>();
    }

    [Test]
    public async Task NoGrantedScopes_Validating_ReturnsValidatedEmptyScopeSet()
    {
        var factory = new ScriptedHttpClientFactory();
        factory.Respond(_ => JsonResponse("""{"user_id":"123","login":"Streamer","scopes":[]}"""));
        var client = new OAuthTransport(factory);

        var validation = (await client.ValidateTokenAsync("access", CancellationToken.None))
            .ShouldBeOfType<TokenValidationOutcome.Validated>()
            .Validation;

        validation.Scopes.ShouldBeEmpty();
        validation.Scopes.ShouldBeSameAs(OAuthScopeSet.Empty);
    }

    [Test]
    public async Task ProviderFailure_Validating_RemainsExceptional()
    {
        var factory = new ScriptedHttpClientFactory();
        factory.Respond(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        factory.Respond(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var client = new OAuthTransport(factory);

        await Should.ThrowAsync<HttpRequestException>(() =>
            client.ValidateTokenAsync("limited", CancellationToken.None)
        );
        await Should.ThrowAsync<HttpRequestException>(() =>
            client.ValidateTokenAsync("failed", CancellationToken.None)
        );
    }

    [Test]
    public async Task MalformedValidationPayload_Validating_RemainsExceptional()
    {
        var factory = new ScriptedHttpClientFactory();
        factory.Respond(_ => JsonResponse("{}"));
        var client = new OAuthTransport(factory);

        await Should.ThrowAsync<JsonException>(() =>
            client.ValidateTokenAsync("malformed", CancellationToken.None)
        );
    }

    private static string ReadContent(HttpRequestMessage request)
    {
        return request.Content?.ReadAsStringAsync(CancellationToken.None).GetAwaiter().GetResult()
            ?? string.Empty;
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }

    private sealed class ScriptedHttpClientFactory : IHttpClientFactory
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();

        public void Respond(Func<HttpRequestMessage, HttpResponseMessage> response)
        {
            _responses.Enqueue(response);
        }

        public HttpClient CreateClient(string name)
        {
            return new(new Handler(_responses), disposeHandler: false);
        }

        private sealed class Handler(Queue<Func<HttpRequestMessage, HttpResponseMessage>> responses)
            : HttpMessageHandler
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
