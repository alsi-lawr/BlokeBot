using System.Buffers;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using BlokeBot.Simulation.FakeTwitch;
using BlokeBot.Twitch;
using BlokeBot.Twitch.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Simulation.Tests;

public sealed class FakeTwitchIntegrationTests
{
    [Test]
    public async Task ReadyScenario_NormalOAuthHelixEventSubAndChatClients_UseOneDeterministicAuthority()
    {
        await using var host = await FakeTwitchHost.StartAsync();
        var transport = new OAuthTransport(host.HttpClientFactory, host.Endpoints);
        var authorize = transport.CreateAuthorizationUri(
            new AuthorizationUriRequest(
                FakeTwitchScenarioDefinition.ReadyDashboard.ClientId,
                "https://callback.invalid/auth/twitch/callback",
                OAuthAuthorizationScopeSet.Create(["user:read:chat", "user:write:chat"]),
                "state-0001",
                AuthorizationVerificationPolicy.ForceAccountVerification
            )
        );

        using var redirect = await host.GetWithoutRedirectAsync(authorize);
        redirect.StatusCode.ShouldBe(HttpStatusCode.Found);
        var code = QueryValue(redirect.Headers.Location.ShouldNotBeNull().AbsoluteUri, "code");

        var token = await transport.ExchangeCodeAsync(
            new AuthorizationCodeExchange(
                FakeTwitchScenarioDefinition.ReadyDashboard.ClientId,
                "fake-secret",
                "https://callback.invalid/auth/twitch/callback",
                code
            ),
            CancellationToken.None
        );
        var validation = await transport.ValidateTokenAsync(
            token.AccessToken,
            CancellationToken.None
        );
        var validated = validation.ShouldBeOfType<TokenValidationOutcome.Validated>();
        validated.Validation.UserId.ShouldBe("1000");
        validated.Validation.Login.ShouldBe("samplechannel");
        validated.Validation.Scopes.ShouldContain("user:write:chat");

        var refreshed = await transport.RefreshCompleteTokenSetAsync(
            FakeTwitchScenarioDefinition.ReadyDashboard.ClientId,
            "fake-secret",
            token.RefreshToken,
            CancellationToken.None
        );
        refreshed.AccessToken.ShouldNotBe(token.AccessToken);
        refreshed.RefreshToken.ShouldNotBe(token.RefreshToken);

        var appToken = await new AppAccessTokenProvider(
            host.HttpClientFactory,
            new BotIdentity
            {
                BotUsername = "blokebot",
                ClientId = FakeTwitchScenarioDefinition.ReadyDashboard.ClientId,
                ClientSecret = "fake-secret",
                RedirectUri = "https://callback.invalid/bot/callback",
                Scopes = OAuthAuthorizationScopeSet.Create(["user:read:chat"]),
                TokenCachePath = "unused",
            },
            host.Endpoints
        ).GetAccessTokenAsync(CancellationToken.None);
        appToken.ShouldBe("fake-app-token");

        var context = new HelixRequestContext(
            FakeTwitchScenarioDefinition.ReadyDashboard.ClientId,
            refreshed.AccessToken
        );
        var helix = new HelixClient(host.HttpClientFactory, host.Endpoints);
        var user = await helix.GetCurrentUserAsync(context, CancellationToken.None);
        user.ShouldNotBeNull();
        user.Id.ShouldBe("1000");
        user.BroadcasterType.ShouldBe("affiliate");
        var stream = await helix.GetStreamAsync(context, "samplechannel", CancellationToken.None);
        stream.ShouldNotBeNull();
        stream.ViewerCount.ShouldBe(42);

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(
            host.Endpoints.InitialEventSubWebSocketEndpoint,
            CancellationToken.None
        );
        var welcome = JsonDocument.Parse(await ReceiveTextAsync(socket));
        var sessionId = welcome
            .RootElement.GetProperty("payload")
            .GetProperty("session")
            .GetProperty("id")
            .GetString();
        sessionId.ShouldNotBeNullOrWhiteSpace();

        var eventSub = new EventSubClient(host.HttpClientFactory, host.Endpoints);
        var chatSubscriptionId = await eventSub.CreateChatMessageSubscriptionAsync(
            context,
            "1000",
            "2000",
            sessionId,
            CancellationToken.None
        );
        chatSubscriptionId.ShouldStartWith("ready-dashboard-subscription-");

        var ordinary = JsonDocument.Parse(await ReceiveTextAsync(socket));
        ordinary
            .RootElement.GetProperty("metadata")
            .GetProperty("subscription_type")
            .GetString()
            .ShouldBe("channel.chat.message");
        ordinary
            .RootElement.GetProperty("payload")
            .GetProperty("event")
            .GetProperty("message")
            .GetProperty("text")
            .GetString()
            .ShouldBe("!hello");
        var moderator = JsonDocument.Parse(await ReceiveTextAsync(socket));
        moderator
            .RootElement.GetProperty("payload")
            .GetProperty("event")
            .GetProperty("badges")[0]
            .GetProperty("set_id")
            .GetString()
            .ShouldBe("moderator");

        var pollSubscriptionId = await eventSub.CreatePollSubscriptionAsync(
            context,
            "channel.poll.begin",
            "1000",
            sessionId,
            CancellationToken.None
        );
        pollSubscriptionId.ShouldStartWith("ready-dashboard-subscription-");
        var poll = JsonDocument.Parse(await ReceiveTextAsync(socket));
        poll.RootElement.GetProperty("metadata")
            .GetProperty("subscription_type")
            .GetString()
            .ShouldBe("channel.poll.begin");

        var chat = new ChatClient(host.HttpClientFactory, host.Endpoints);
        var response = await chat.SendMessageAsync(
            context,
            "1000",
            "1000",
            "Hello from the normal chat client.",
            CancellationToken.None
        );
        response.IsSent.ShouldBeTrue();
        host.Authority.Transcript.ShouldContain(entry =>
            entry.Kind == "helix.chat.message"
            && entry.Detail == "Hello from the normal chat client."
        );

        await Should.ThrowAsync<HttpRequestException>(() =>
            transport.ExchangeCodeAsync(
                new AuthorizationCodeExchange(
                    FakeTwitchScenarioDefinition.ReadyDashboard.ClientId,
                    "fake-secret",
                    "https://callback.invalid/auth/twitch/callback",
                    code
                ),
                CancellationToken.None
            )
        );
    }

    [Test]
    public void SameScenarioDefinition_InitializingAuthorities_ProducesStableLogicalStateAndTranscript()
    {
        var first = new FakeTwitchAuthority(FakeTwitchScenarioDefinition.ReadyDashboard);
        var second = new FakeTwitchAuthority(FakeTwitchScenarioDefinition.ReadyDashboard);
        var scopes = new HashSet<string>(StringComparer.Ordinal) { "user:read:chat" };

        first
            .Authorize("fake-twitch-client", "https://callback.invalid/", scopes)
            .ShouldBe(second.Authorize("fake-twitch-client", "https://callback.invalid/", scopes));
        first.Transcript.ShouldBe(second.Transcript);
    }

    [Test]
    public async Task InvalidScopeTokenAndRoute_ReturnDeterministicVisibleFailures()
    {
        await using var host = await FakeTwitchHost.StartAsync();
        using var client = new HttpClient();

        using var denied = await client.GetAsync(
            $"{host.HttpAddress}oauth2/authorize?response_type=code&client_id=fake-twitch-client&redirect_uri=https%3A%2F%2Fcallback.invalid%2F&state=s&scope=channel%3Amanage%3Abroadcast"
        );
        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var invalidToken = await client.GetAsync($"{host.HttpAddress}helix/users");
        invalidToken.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        using var unsupported = await client.GetAsync($"{host.HttpAddress}helix/not-implemented");
        unsupported.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await unsupported.Content.ReadAsStringAsync()).ShouldContain("unsupported_route");
    }

    private static string QueryValue(string uri, string key)
    {
        var query = new Uri(uri)
            .Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries);
        return Uri.UnescapeDataString(
            query.Select(pair => pair.Split('=', 2)).Single(pair => pair[0] == key)[1]
        );
    }

    private static async Task<string> ReceiveTextAsync(ClientWebSocket socket)
    {
        var payload = new ArrayBufferWriter<byte>();
        var buffer = new byte[2048];
        do
        {
            var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
            result.MessageType.ShouldBe(WebSocketMessageType.Text);
            payload.Write(buffer.AsSpan(0, result.Count));
            if (result.EndOfMessage)
            {
                break;
            }
        } while (true);

        return Encoding.UTF8.GetString(payload.WrittenSpan);
    }

    private sealed class FakeTwitchHost(
        WebApplication app,
        string httpAddress,
        TwitchEndpointPolicy endpoints,
        FakeTwitchAuthority authority
    ) : IAsyncDisposable
    {
        public FakeTwitchAuthority Authority { get; } = authority;

        public TwitchEndpointPolicy Endpoints { get; } = endpoints;

        public IHttpClientFactory HttpClientFactory { get; } = new LoopbackHttpClientFactory();

        public string HttpAddress { get; } = httpAddress;

        public static async Task<FakeTwitchHost> StartAsync()
        {
            var builder = WebApplication.CreateBuilder();
            builder.Services.AddFakeTwitch(FakeTwitchScenarioDefinition.ReadyDashboard);
            var app = builder.Build();
            app.Urls.Add("http://127.0.0.1:0");
            app.MapFakeTwitch();
            await app.StartAsync();

            var address =
                app.Services.GetRequiredService<IServer>()
                    .Features.Get<IServerAddressesFeature>()
                    ?.Addresses.ShouldHaveSingleItem()
                ?? throw new InvalidOperationException("The fake host did not publish an address.");
            var httpAddress = address.TrimEnd('/') + "/";
            var httpUri = new Uri(httpAddress);
            var endpoints = new TwitchEndpointPolicy
            {
                OAuthOrigin = new Uri(httpUri, "oauth2/"),
                HelixOrigin = new Uri(httpUri, "helix/"),
                EventSubWebSocketUri = new UriBuilder(httpUri) { Scheme = "ws", Path = "ws" }.Uri,
            };
            return new(
                app,
                httpAddress,
                endpoints,
                app.Services.GetRequiredService<FakeTwitchAuthority>()
            );
        }

        public async Task<HttpResponseMessage> GetWithoutRedirectAsync(Uri uri)
        {
            using var client = new HttpClient(
                new HttpClientHandler { AllowAutoRedirect = false },
                disposeHandler: true
            );
            return await client.GetAsync(uri);
        }

        public async ValueTask DisposeAsync()
        {
            await app.DisposeAsync();
        }
    }

    private sealed class LoopbackHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new HttpClient();
        }
    }
}
