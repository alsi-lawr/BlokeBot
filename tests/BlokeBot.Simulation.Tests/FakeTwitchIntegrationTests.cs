using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BlokeBot.Simulation.FakeTwitch;
using BlokeBot.Twitch;
using BlokeBot.Twitch.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

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

        using var appProvider = new AppAccessTokenProvider(
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
        );
        var appToken = await appProvider.GetAccessTokenAsync(CancellationToken.None);
        appToken.ShouldBe("fake-app-token");

        var context = new HelixRequestContext(
            FakeTwitchScenarioDefinition.ReadyDashboard.ClientId,
            refreshed.AccessToken
        );
        var helix = new HelixClient(host.HttpClientFactory, host.Endpoints);
        var user = await helix.GetCurrentUserAsync(context, CancellationToken.None);
        _ = user.ShouldNotBeNull();
        user.Id.ShouldBe("1000");
        user.BroadcasterType.ShouldBe("affiliate");
        var stream = await helix.GetStreamAsync(
            new HelixRequestContext(FakeTwitchScenarioDefinition.ReadyDashboard.ClientId, appToken),
            "samplechannel",
            CancellationToken.None
        );
        _ = stream.ShouldNotBeNull();
        stream.ViewerCount.ShouldBe(42);
        var broadcasterContext = new HelixRequestContext(
            FakeTwitchScenarioDefinition.ReadyDashboard.ClientId,
            FakeTwitchAuthority.BroadcasterAccessToken
        );
        var clip = await helix.CreateClipAsync(
            broadcasterContext,
            "1000",
            false,
            CancellationToken.None
        );
        var createdClip = clip.ShouldBeOfType<HelixClipCreateOutcome.Created>().Clip;
        var clipLookup = await helix.GetClipAsync(
            broadcasterContext,
            createdClip.Id,
            CancellationToken.None
        );
        clipLookup
            .ShouldBeOfType<HelixClipLookupOutcome.Found>()
            .Clip.Url.ShouldStartWith("https://clips.twitch.tv/fake-clip-");
        var marker = await helix.CreateStreamMarkerAsync(
            broadcasterContext,
            "1000",
            "Fake marker",
            CancellationToken.None
        );
        var createdMarker = marker.ShouldBeOfType<HelixStreamMarkerCreateOutcome.Created>().Marker;
        var markerLookup = await helix.GetStreamMarkersAsync(
            broadcasterContext,
            "1000",
            new HashSet<string>(StringComparer.Ordinal) { createdMarker.Id },
            CancellationToken.None
        );
        markerLookup
            .ShouldBeOfType<HelixStreamMarkerLookupOutcome.Found>()
            .Markers.ShouldHaveSingleItem()
            .Id.ShouldBe(createdMarker.Id);

        await using var webhookReceiver = await FakeWebhookReceiver.StartAsync(
            "fake-eventsub-secret"
        );
        var webhook = new EventSubWebhookOptions
        {
            CallbackUri = webhookReceiver.Address,
            Secret = "fake-eventsub-secret",
        };
        using var appAccessTokens = new AppAccessTokenProvider(
            host.HttpClientFactory,
            PublicChatIdentity(),
            host.Endpoints
        );
        var eventSub = new EventSubClient(
            host.HttpClientFactory,
            host.Endpoints,
            webhook,
            appAccessTokens,
            new ImmediateVerification()
        );
        var subscriptionId = await eventSub.CreateAsync(
            FakeTwitchScenarioDefinition.ReadyDashboard.ClientId,
            new EventSubSubscriptionRequest(
                "channel.chat.message",
                "1",
                new Dictionary<string, string>
                {
                    ["broadcaster_user_id"] = "1000",
                    ["user_id"] = "2000",
                }
            ),
            CancellationToken.None
        );
        subscriptionId.ShouldStartWith("ready-dashboard-subscription-");
        var inventory = await eventSub.ListSubscriptionsAsync(
            FakeTwitchScenarioDefinition.ReadyDashboard.ClientId,
            null,
            CancellationToken.None
        );
        var subscription = inventory.Subscriptions.ShouldHaveSingleItem();
        subscription.Id.ShouldBe(subscriptionId);
        subscription.Method.ShouldBe("webhook");
        subscription.Callback.ShouldBe(webhook.CallbackUri.AbsoluteUri);
        await webhookReceiver.WaitForAsync("webhook_callback_verification");
        await webhookReceiver.WaitForAsync("notification");
        foreach (var delivery in webhookReceiver.Deliveries)
        {
            delivery.SignatureValid.ShouldBeTrue();
            delivery.SubscriptionMethod.ShouldBe("webhook");
        }
        await eventSub.DeleteAsync(
            FakeTwitchScenarioDefinition.ReadyDashboard.ClientId,
            subscriptionId,
            CancellationToken.None
        );
        (
            await eventSub.ListSubscriptionsAsync(
                FakeTwitchScenarioDefinition.ReadyDashboard.ClientId,
                null,
                CancellationToken.None
            )
        ).Subscriptions.ShouldBeEmpty();

        _ = await Should.ThrowAsync<HttpRequestException>(() =>
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
    public async Task EventSubLifecycle_SignsDuplicateAndRevocationThenResetsMixedUserSubscriptionsOnRestart()
    {
        await using var host = await FakeTwitchHost.StartAsync();
        await using var webhookReceiver = await FakeWebhookReceiver.StartAsync(
            "fake-eventsub-secret"
        );
        var webhook = new EventSubWebhookOptions
        {
            CallbackUri = webhookReceiver.Address,
            Secret = "fake-eventsub-secret",
        };
        using var appAccessTokens = new AppAccessTokenProvider(
            host.HttpClientFactory,
            PublicChatIdentity(),
            host.Endpoints
        );
        var eventSub = new EventSubClient(
            host.HttpClientFactory,
            host.Endpoints,
            webhook,
            appAccessTokens,
            new ImmediateVerification()
        );

        var botSubscriptionId = await eventSub.CreateAsync(
            FakeTwitchScenarioDefinition.ReadyDashboard.ClientId,
            new EventSubSubscriptionRequest(
                "channel.chat.message",
                "1",
                new Dictionary<string, string>
                {
                    ["broadcaster_user_id"] = "1000",
                    ["user_id"] = "2000",
                }
            ),
            CancellationToken.None
        );
        var broadcasterSubscriptionId = await eventSub.CreateAsync(
            FakeTwitchScenarioDefinition.ReadyDashboard.ClientId,
            new EventSubSubscriptionRequest(
                "channel.poll.begin",
                "1",
                new Dictionary<string, string> { ["broadcaster_user_id"] = "1000" }
            ),
            CancellationToken.None
        );
        await webhookReceiver.WaitForCountAsync("webhook_callback_verification", 2);
        await webhookReceiver.WaitForCountAsync("notification", 4);

        var mixedInventory = (
            await eventSub.ListSubscriptionsAsync(
                FakeTwitchScenarioDefinition.ReadyDashboard.ClientId,
                null,
                CancellationToken.None
            )
        ).Subscriptions.ToDictionary(static subscription => subscription.Id);
        mixedInventory[botSubscriptionId]
            .Condition.ShouldBe(
                new Dictionary<string, string>
                {
                    ["broadcaster_user_id"] = "1000",
                    ["user_id"] = "2000",
                }
            );
        mixedInventory[broadcasterSubscriptionId]
            .Condition.ShouldBe(
                new Dictionary<string, string> { ["broadcaster_user_id"] = "1000" }
            );

        await host.Authority.DeliverDuplicateNotificationAsync(
            botSubscriptionId,
            CancellationToken.None
        );
        var duplicateId = $"fake-eventsub-duplicate-{botSubscriptionId}";
        await webhookReceiver.WaitForMessageCountAsync(duplicateId, 2);
        var duplicates = webhookReceiver
            .Deliveries.Where(delivery => delivery.MessageId == duplicateId)
            .ToArray();
        duplicates.Length.ShouldBe(2);
        duplicates.ShouldAllBe(static delivery => delivery.SignatureValid);
        duplicates.Select(static delivery => delivery.BodyHash).Distinct().Count().ShouldBe(1);

        await host.Authority.RevokeAuthorizationAsync(
            broadcasterSubscriptionId,
            CancellationToken.None
        );
        var revocationId = $"fake-eventsub-revocation-{broadcasterSubscriptionId}";
        await webhookReceiver.WaitForMessageCountAsync(revocationId, 1);
        var revocation = webhookReceiver.Deliveries.Single(delivery =>
            delivery.MessageId == revocationId
        );
        revocation.MessageType.ShouldBe("revocation");
        revocation.SignatureValid.ShouldBeTrue();
        revocation.SubscriptionId.ShouldBe(broadcasterSubscriptionId);
        revocation.SubscriptionStatus.ShouldBe("authorization_revoked");
        (
            await eventSub.ListEnabledOwnedIdsAsync(
                FakeTwitchScenarioDefinition.ReadyDashboard.ClientId,
                CancellationToken.None
            )
        ).ShouldBe(new HashSet<string>(StringComparer.Ordinal) { botSubscriptionId });

        var restarted = new EventSubClient(
            host.HttpClientFactory,
            host.Endpoints,
            webhook,
            appAccessTokens,
            new ImmediateVerification()
        );
        await restarted.ResetAsync(
            FakeTwitchScenarioDefinition.ReadyDashboard.ClientId,
            CancellationToken.None
        );
        host.Authority.ActiveSubscriptions.ShouldBeEmpty();
        var restartedSubscriptionId = await restarted.CreateAsync(
            FakeTwitchScenarioDefinition.ReadyDashboard.ClientId,
            new EventSubSubscriptionRequest(
                "channel.chat.message",
                "1",
                new Dictionary<string, string>
                {
                    ["broadcaster_user_id"] = "1000",
                    ["user_id"] = "2000",
                }
            ),
            CancellationToken.None
        );
        restartedSubscriptionId.ShouldNotBe(botSubscriptionId);
        await webhookReceiver.WaitForCountAsync("webhook_callback_verification", 3);
        host.Authority.ActiveSubscriptions.ShouldHaveSingleItem()
            .Id.ShouldBe(restartedSubscriptionId);
        webhookReceiver.Deliveries.ShouldAllBe(static delivery => delivery.SignatureValid);
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
    public async Task ReadyScenario_ProfileImageUrl_ReturnsDeterministicLoadableAvatar()
    {
        await using var host = await FakeTwitchHost.StartAsync();
        var helix = new HelixClient(host.HttpClientFactory, host.Endpoints);
        var context = new HelixRequestContext(
            FakeTwitchScenarioDefinition.ReadyDashboard.ClientId,
            FakeTwitchAuthority.BroadcasterAccessToken
        );

        var user = await helix.GetCurrentUserAsync(context, CancellationToken.None);

        _ = user.ShouldNotBeNull();
        user.ProfileImageUrl.ShouldBe($"{host.HttpAddress}profile-images/{user.Login}.svg");
        using var client = new HttpClient();
        using var first = await client.GetAsync(user.ProfileImageUrl);
        using var second = await client.GetAsync(user.ProfileImageUrl);
        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        first.Content.Headers.ContentType?.MediaType.ShouldBe("image/svg+xml");
        var firstAvatar = await first.Content.ReadAsByteArrayAsync();
        firstAvatar.Length.ShouldBeGreaterThan(0);
        firstAvatar.ShouldBe(await second.Content.ReadAsByteArrayAsync());
        Encoding.UTF8.GetString(firstAvatar).ShouldContain("width=\"64\" height=\"64\"");

        using var missing = await client.GetAsync(
            $"{host.HttpAddress}profile-images/not-a-user.svg"
        );
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task InvalidScopeTokenAndRoute_ReturnDeterministicVisibleFailures()
    {
        await using var host = await FakeTwitchHost.StartAsync();
        using var client = new HttpClient();

        using var denied = await client.GetAsync(
            $"{host.HttpAddress}oauth2/authorize?response_type=code&client_id=fake-twitch-client&redirect_uri=https%3A%2F%2Fcallback.invalid%2F&state=s&scope=channel%3Amanage%3Aads"
        );
        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var invalidToken = await client.GetAsync($"{host.HttpAddress}helix/users");
        invalidToken.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        using var unsupported = await client.GetAsync($"{host.HttpAddress}helix/not-implemented");
        unsupported.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await unsupported.Content.ReadAsStringAsync()).ShouldContain("unsupported_route");
        using var oldSocket = await client.GetAsync($"{host.HttpAddress}eventsub");
        oldSocket.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        using var invalidSubscription = new HttpRequestMessage(
            HttpMethod.Post,
            $"{host.HttpAddress}helix/eventsub/subscriptions"
        )
        {
            Content = new StringContent(
                """
                {
                  "type":"channel.chat.message",
                  "version":"1",
                  "condition":{"broadcaster_user_id":"1000","user_id":"not-the-bot"},
                  "transport":{"method":"http","callback":"https://callback.invalid/eventsub"}
                }
                """,
                Encoding.UTF8,
                "application/json"
            ),
        };
        invalidSubscription.Headers.Add("Client-Id", "fake-twitch-client");
        invalidSubscription.Headers.Authorization = new(
            "Bearer",
            FakeTwitchAuthority.BotAccessToken
        );
        using var invalidSubscriptionResponse = await client.SendAsync(invalidSubscription);
        invalidSubscriptionResponse.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private static BotIdentity PublicChatIdentity() =>
        new()
        {
            BotUsername = "blokebot",
            ClientId = FakeTwitchScenarioDefinition.ReadyDashboard.ClientId,
            ClientSecret = "fake-secret",
            RedirectUri = "https://callback.invalid/bot/callback",
            Scopes = OAuthAuthorizationScopeSet.Create(["user:read:chat"]),
            TokenCachePath = "unused",
        };

    private static string QueryValue(string uri, string key)
    {
        var query = new Uri(uri)
            .Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries);
        return Uri.UnescapeDataString(
            query.Select(pair => pair.Split('=', 2)).Single(pair => pair[0] == key)[1]
        );
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
            _ = builder.Services.AddFakeTwitch(FakeTwitchScenarioDefinition.ReadyDashboard);
            var app = builder.Build();
            app.Urls.Add("http://127.0.0.1:0");
            _ = app.MapFakeTwitch();
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

        public async ValueTask DisposeAsync() => await app.DisposeAsync();
    }

    private sealed class LoopbackHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new HttpClient();
    }

    private sealed class ImmediateVerification : IEventSubSubscriptionVerification
    {
        public Task WaitAsync(string subscriptionId, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public void Confirm(string subscriptionId) { }
    }

    private sealed class FakeWebhookReceiver(
        WebApplication app,
        Uri address,
        ConcurrentQueue<WebhookDelivery> deliveries
    ) : IAsyncDisposable
    {
        public Uri Address { get; } = address;

        internal IReadOnlyList<WebhookDelivery> Deliveries => deliveries.ToArray();

        internal static async Task<FakeWebhookReceiver> StartAsync(string secret)
        {
            var deliveries = new ConcurrentQueue<WebhookDelivery>();
            var builder = WebApplication.CreateBuilder();
            var app = builder.Build();
            app.Urls.Add("http://127.0.0.1:0");
            _ = app.MapPost(
                "/eventsub/twitch",
                async (HttpRequest request) =>
                {
                    await using var body = new MemoryStream();
                    await request.Body.CopyToAsync(body);
                    var bytes = body.ToArray();
                    var messageId = request.Headers["Twitch-Eventsub-Message-Id"].ToString();
                    var timestamp = request.Headers["Twitch-Eventsub-Message-Timestamp"].ToString();
                    var signature = request.Headers["Twitch-Eventsub-Message-Signature"].ToString();
                    var prefix = Encoding.UTF8.GetBytes(messageId + timestamp);
                    var signed = new byte[prefix.Length + bytes.Length];
                    prefix.CopyTo(signed, 0);
                    bytes.CopyTo(signed, prefix.Length);
                    var expected =
                        "sha256="
                        + Convert
                            .ToHexString(
                                HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), signed)
                            )
                            .ToLowerInvariant();
                    using var document = JsonDocument.Parse(bytes);
                    var messageType = request.Headers["Twitch-Eventsub-Message-Type"].ToString();
                    var subscription = document.RootElement.GetProperty("subscription");
                    deliveries.Enqueue(
                        new WebhookDelivery(
                            messageId,
                            messageType,
                            CryptographicOperations.FixedTimeEquals(
                                Encoding.ASCII.GetBytes(expected),
                                Encoding.ASCII.GetBytes(signature)
                            ),
                            Convert.ToHexString(SHA256.HashData(bytes)),
                            subscription.GetProperty("id").GetString(),
                            subscription.GetProperty("status").GetString(),
                            subscription.GetProperty("transport").GetProperty("method").GetString()
                        )
                    );
                    return messageType == "webhook_callback_verification"
                        ? Results.Text(
                            document.RootElement.GetProperty("challenge").GetString(),
                            "text/plain"
                        )
                        : Results.Accepted();
                }
            );
            await app.StartAsync();
            var serverAddress =
                app.Services.GetRequiredService<IServer>()
                    .Features.Get<IServerAddressesFeature>()
                    ?.Addresses.ShouldHaveSingleItem()
                ?? throw new InvalidOperationException(
                    "The fake webhook receiver did not publish an address."
                );
            var receiver = new FakeWebhookReceiver(
                app,
                new Uri(serverAddress.TrimEnd('/') + "/eventsub/twitch"),
                deliveries
            );
            return receiver;
        }

        internal Task WaitForAsync(string messageType) => WaitForCountAsync(messageType, 1);

        internal async Task WaitForCountAsync(string messageType, int expectedCount)
        {
            var timeout = DateTime.UtcNow.AddSeconds(2);
            while (
                deliveries.Count(delivery => delivery.MessageType == messageType) < expectedCount
                && DateTime.UtcNow < timeout
            )
            {
                await Task.Delay(5);
            }

            deliveries
                .Count(delivery => delivery.MessageType == messageType)
                .ShouldBeGreaterThanOrEqualTo(expectedCount);
        }

        internal async Task WaitForMessageCountAsync(string messageId, int expectedCount)
        {
            var timeout = DateTime.UtcNow.AddSeconds(2);
            while (
                deliveries.Count(delivery => delivery.MessageId == messageId) < expectedCount
                && DateTime.UtcNow < timeout
            )
            {
                await Task.Delay(5);
            }

            deliveries.Count(delivery => delivery.MessageId == messageId).ShouldBe(expectedCount);
        }

        public async ValueTask DisposeAsync() => await app.DisposeAsync();
    }

    private sealed record WebhookDelivery(
        string MessageId,
        string MessageType,
        bool SignatureValid,
        string BodyHash,
        string? SubscriptionId,
        string? SubscriptionStatus,
        string? SubscriptionMethod
    );
}
