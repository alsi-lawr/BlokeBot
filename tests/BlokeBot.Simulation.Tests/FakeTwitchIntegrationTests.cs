using System.Diagnostics;
using System.Net;
using System.Text;
using BlokeBot.Commands;
using BlokeBot.Eventing;
using BlokeBot.Functional;
using BlokeBot.Simulation.FakeTwitch;
using BlokeBot.Twitch;
using BlokeBot.Twitch.Auth;
using BlokeBot.Twitch.Runtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Polly;
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
        var stream = await helix.GetStreamAsync(
            new HelixRequestContext(FakeTwitchScenarioDefinition.ReadyDashboard.ClientId, appToken),
            "samplechannel",
            CancellationToken.None
        );
        stream.ShouldNotBeNull();
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

        var observedChat = new RecordingChatObserver();
        var observedPolls = new RecordingPollObserver();
        var commandSender = new ProductChatCommandResponseSender(CreatePublicChatTransport(host));
        var session = CreateEventSubSession(host, observedChat, observedPolls, commandSender);
        var established = await session.EstablishAsync(
            new RuntimeConnectionTarget.Initial(),
            CancellationToken.None
        );
        var listening = established
            .ShouldBeOfType<RuntimeSessionEstablishment.Established>()
            .Session;
        using var listeningCancellation = new CancellationTokenSource();
        var listeningTask = listening.ListenAsync(listeningCancellation.Token);

        await WaitUntilAsync(() =>
            observedChat.Messages.Count == 3
            && observedPolls.Events.Count == 1
            && host.Authority.Transcript.Any(entry =>
                entry.Kind == "helix.chat.message" && entry.Detail == "normal command response"
            )
        );
        observedChat
            .Messages.Select(message => message.Text)
            .ShouldBe(["!hello", "!mod", "!welcome"]);
        observedChat.Messages[0].Tags.ShouldNotContainKey("mod");
        observedChat.Messages[1].Tags["mod"].ShouldBe("1");
        observedPolls.Events.ShouldHaveSingleItem().PollId.ShouldBe("poll-0001");
        var subscriptions = host.Authority.ActiveSubscriptions;
        var chatSubscription = subscriptions
            .Where(subscription => subscription.Type == "channel.chat.message")
            .ShouldHaveSingleItem();
        chatSubscription.BroadcasterId.ShouldBe("1000");
        chatSubscription.BotUserId.ShouldBe("2000");
        chatSubscription.SubscriberUserId.ShouldBe("2000");
        subscriptions.ShouldContain(subscription =>
            subscription.Type == "channel.poll.begin"
            && subscription.BroadcasterId == "1000"
            && subscription.SubscriberUserId == "1000"
        );

        listeningCancellation.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => listeningTask);
        await listening.DisposeAsync();

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
    public async Task ReadyScenario_ProfileImageUrl_ReturnsDeterministicLoadableAvatar()
    {
        await using var host = await FakeTwitchHost.StartAsync();
        var helix = new HelixClient(host.HttpClientFactory, host.Endpoints);
        var context = new HelixRequestContext(
            FakeTwitchScenarioDefinition.ReadyDashboard.ClientId,
            FakeTwitchAuthority.BroadcasterAccessToken
        );

        var user = await helix.GetCurrentUserAsync(context, CancellationToken.None);

        user.ShouldNotBeNull();
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
                  "transport":{"method":"websocket","session_id":"unused"}
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

    private static EventSubConnectionSession CreateEventSubSession(
        FakeTwitchHost host,
        RecordingChatObserver chatObserver,
        RecordingPollObserver pollObserver,
        ICommandResponseSender responseSender
    )
    {
        var operations = new LoopbackEventSubOperations(host.HttpClientFactory, host.Endpoints);
        var channelStatus = new EventSubChannelStatusStore();
        var runtimeStatus = new BotRuntimeStatusStore();
        var factory = new EventSubChannelSessionFactory(
            operations,
            new EventSubChannelRecoveryPipeline(
                new ResiliencePipelineBuilder().Build(),
                new ResiliencePipelineBuilder<EventSubChannelReconciliationOutcome>().Build()
            ),
            new EventSubSubscriptionReconciliationStore(),
            channelStatus,
            runtimeStatus,
            new NoOpDiagnosticReporter(),
            TimeProvider.System
        );
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddContinueAndReportObserverFanOut<
            EventSubMessageObserverBoundary,
            ChatMessage,
            ChatObserverDeadLetter
        >(BotObserverBoundaries.EventSubMessages);
        services.AddChatCommands().AddCommandModule<ReplyCommandModule>();
        using var provider = services.BuildServiceProvider();
        return new EventSubConnectionSession(
            new StaticChannelProvider(),
            factory,
            provider.GetRequiredService<ChatCommandDispatcher>(),
            responseSender,
            runtimeStatus,
            new AlwaysEnabledNativeTwitch(),
            new EventSubChannelReconciliationTrigger(null!),
            [chatObserver],
            provider.GetRequiredService<
                ObserverFanOut<EventSubMessageObserverBoundary, ChatMessage, ChatObserverDeadLetter>
            >(),
            NullLogger<EventSubConnectionSession>.Instance,
            host.Endpoints,
            pollObservers: [pollObserver]
        );
    }

    private static HelixPublicChatTransport CreatePublicChatTransport(FakeTwitchHost host)
    {
        var identity = new BotIdentity
        {
            BotUsername = "blokebot",
            ClientId = FakeTwitchScenarioDefinition.ReadyDashboard.ClientId,
            ClientSecret = "fake-secret",
            RedirectUri = "https://callback.invalid/bot/callback",
            Scopes = OAuthAuthorizationScopeSet.Create(["user:read:chat"]),
            TokenCachePath = "unused",
        };
        return new(
            new AppAccessTokenProvider(host.HttpClientFactory, identity, host.Endpoints),
            new StaticBotAccountProvider(),
            identity,
            new ChatIdentityResolver(
                identity,
                new HelixClient(host.HttpClientFactory, host.Endpoints)
            ),
            new ChatClient(host.HttpClientFactory, host.Endpoints),
            NullLogger<HelixPublicChatTransport>.Instance
        );
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(3);
        while (!condition())
        {
            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new TimeoutException("The fake EventSub delivery did not complete.");
            }

            await Task.Delay(10);
        }
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

    private sealed class ReplyCommandModule : IChatCommandModule
    {
        public void AddCommands(IChatCommandBuilder commands)
        {
            commands.Map(
                "hello",
                async (context, _, cancellationToken) =>
                    await context.ReplyAsync("normal command response", cancellationToken)
            );
        }
    }

    private sealed class RecordingChatObserver : IChatMessageObserver
    {
        public List<ChatMessage> Messages { get; } = [];

        public ValueTask MessageReceivedAsync(
            ChatMessage message,
            CancellationToken cancellationToken
        )
        {
            Messages.Add(message);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingPollObserver : IPollEventObserver
    {
        public List<EventSubPollEvent> Events { get; } = [];

        public Task PollReceivedAsync(EventSubPollEvent poll, CancellationToken cancellationToken)
        {
            Events.Add(poll);
            return Task.CompletedTask;
        }
    }

    private sealed class ProductChatCommandResponseSender(HelixPublicChatTransport transport)
        : ICommandResponseSender
    {
        public async ValueTask SendAsync(
            ChatMessage sourceMessage,
            CommandResponse response,
            CancellationToken cancellationToken
        )
        {
            var preparation = await transport.PrepareAsync(
                new PublicChatClaimedMessage
                {
                    Id = 1,
                    Channel = sourceMessage.Channel,
                    Message = response.Message,
                    EnqueuedAt = DateTimeOffset.UnixEpoch,
                    ExpiresAt = DateTimeOffset.UnixEpoch.AddMinutes(1),
                    Attempt = 1,
                    ClaimToken = new PublicChatClaimToken(Guid.Empty),
                    ClaimExpiresAt = DateTimeOffset.UnixEpoch.AddMinutes(1),
                    DeduplicationKey = new PublicChatDeduplicationKey("fake-command-response"),
                },
                cancellationToken
            );
            var ready = preparation.ShouldBeOfType<PublicChatPreparationOutcome.Ready>();
            _ = await transport.SendAsync(ready.Send, cancellationToken);
        }
    }

    private sealed class StaticChannelProvider : IBotChannelProvider
    {
        public ValueTask<IReadOnlyList<string>> GetChannelsAsync(
            CancellationToken cancellationToken
        )
        {
            return ValueTask.FromResult<IReadOnlyList<string>>(["samplechannel"]);
        }
    }

    private sealed class StaticBotAccountProvider : IBotAccountProvider
    {
        public IO<BotAccount, AccessTokenUnavailableReason> GetBotAccount(string channelLogin)
        {
            return IO<BotAccount, AccessTokenUnavailableReason>.Create(_ =>
                ValueTask.FromResult(
                    Result<BotAccount, AccessTokenUnavailableReason>.Success(
                        new BotAccount("blokebot", FakeTwitchAuthority.BotAccessToken)
                    )
                )
            );
        }
    }

    private sealed class LoopbackEventSubOperations(
        IHttpClientFactory clients,
        TwitchEndpointPolicy endpoints
    ) : IEventSubChannelOperations
    {
        private readonly EventSubClient _eventSub = new(clients, endpoints);

        public IO<BotAccount, AccessTokenUnavailableReason> ResolveAccount(
            string channel,
            EventSubAuthorizationContext authorization
        )
        {
            return authorization.Match(
                _ => Success("blokebot", FakeTwitchAuthority.BotAccessToken),
                _ => Success("blokebot", FakeTwitchAuthority.BotAccessToken),
                _ => Success("samplechannel", FakeTwitchAuthority.BroadcasterAccessToken)
            );
        }

        public async ValueTask<EventSubSubscriptionSetupOutcome> CreateSubscriptionAsync(
            string channel,
            EventSubAuthorizationContext authorization,
            BotAccount account,
            string sessionId,
            CancellationToken cancellationToken,
            EventSubOperationSubscriptionKind? operationKind = null
        )
        {
            var context = new HelixRequestContext(
                FakeTwitchScenarioDefinition.ReadyDashboard.ClientId,
                account.AccessToken
            );
            EventSubSubscriptionRequest[] subscriptions = operationKind switch
            {
                null =>
                [
                    new EventSubSubscriptionRequest(
                        "channel.chat.message",
                        "1",
                        new Dictionary<string, string>
                        {
                            ["broadcaster_user_id"] = "1000",
                            ["user_id"] = "2000",
                        },
                        sessionId
                    ),
                ],
                EventSubOperationSubscriptionKind.Shoutouts =>
                [
                    BroadcasterAndModerator("channel.shoutout.create", sessionId),
                    BroadcasterAndModerator("channel.shoutout.receive", sessionId),
                ],
                EventSubOperationSubscriptionKind.Raids =>
                [
                    new EventSubSubscriptionRequest(
                        "channel.raid",
                        "1",
                        new Dictionary<string, string> { ["to_broadcaster_user_id"] = "1000" },
                        sessionId
                    ),
                ],
                EventSubOperationSubscriptionKind.Polls =>
                [
                    BroadcasterOnly("channel.poll.begin", sessionId),
                    BroadcasterOnly("channel.poll.progress", sessionId),
                    BroadcasterOnly("channel.poll.end", sessionId),
                ],
                EventSubOperationSubscriptionKind.RewardRedemptions =>
                [
                    BroadcasterOnly(
                        "channel.channel_points_custom_reward_redemption.add",
                        sessionId
                    ),
                    BroadcasterOnly(
                        "channel.channel_points_custom_reward_redemption.update",
                        sessionId
                    ),
                ],
                EventSubOperationSubscriptionKind.Predictions =>
                [
                    BroadcasterOnly("channel.prediction.begin", sessionId),
                    BroadcasterOnly("channel.prediction.progress", sessionId),
                    BroadcasterOnly("channel.prediction.lock", sessionId),
                    BroadcasterOnly("channel.prediction.end", sessionId),
                ],
                _ => throw new UnreachableException(),
            };
            var subscriptionIds = new List<string>(subscriptions.Length);
            foreach (var subscription in subscriptions)
            {
                subscriptionIds.Add(
                    await _eventSub.CreateSubscriptionAsync(
                        context,
                        subscription,
                        cancellationToken
                    )
                );
            }

            return new EventSubSubscriptionSetupOutcome.Created(
                new ActiveEventSubSubscription
                {
                    Channel = channel,
                    SubscriptionId = subscriptionIds[0],
                    BotLogin = account.Login,
                    AccessToken = account.AccessToken,
                    Authorization = authorization,
                    Readiness = EventSubSubscriptionReadiness.Ready,
                    AdditionalSubscriptionIds = subscriptionIds.Skip(1).ToArray(),
                }
            );
        }

        private static EventSubSubscriptionRequest BroadcasterAndModerator(
            string type,
            string sessionId
        )
        {
            return new(
                type,
                "1",
                new Dictionary<string, string>
                {
                    ["broadcaster_user_id"] = "1000",
                    ["moderator_user_id"] = "2000",
                },
                sessionId
            );
        }

        private static EventSubSubscriptionRequest BroadcasterOnly(string type, string sessionId)
        {
            return new(
                type,
                "1",
                new Dictionary<string, string> { ["broadcaster_user_id"] = "1000" },
                sessionId
            );
        }

        public ValueTask<bool> NativeTwitchFeatureIsEnabledAsync(
            string channel,
            EventSubOperationSubscriptionKind kind,
            CancellationToken cancellationToken
        )
        {
            return ValueTask.FromResult(true);
        }

        public ValueTask<EventSubStartupDeliveryOutcome> DeliverStartupMessageAsync(
            string channel,
            CancellationToken cancellationToken
        )
        {
            return ValueTask.FromResult<EventSubStartupDeliveryOutcome>(
                new EventSubStartupDeliveryOutcome.Completed()
            );
        }

        public ValueTask NotifyChannelStartedAsync(
            string channel,
            CancellationToken cancellationToken
        )
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask<EventSubSubscriptionDeletionOutcome> DeleteSubscriptionAsync(
            ActiveEventSubSubscription subscription,
            CancellationToken cancellationToken
        )
        {
            return ValueTask.FromResult<EventSubSubscriptionDeletionOutcome>(
                new EventSubSubscriptionDeletionOutcome.Deleted()
            );
        }

        public ValueTask CompleteStopAsync(string channel, CancellationToken cancellationToken)
        {
            return ValueTask.CompletedTask;
        }

        private static IO<BotAccount, AccessTokenUnavailableReason> Success(
            string login,
            string accessToken
        )
        {
            return IO<BotAccount, AccessTokenUnavailableReason>.Create(_ =>
                ValueTask.FromResult(
                    Result<BotAccount, AccessTokenUnavailableReason>.Success(
                        new BotAccount(login, accessToken)
                    )
                )
            );
        }
    }

    private sealed class AlwaysEnabledNativeTwitch : INativeTwitchFeatureStateProvider
    {
        public ValueTask<bool> IsEnabledAsync(
            string channel,
            NativeTwitchFeature feature,
            CancellationToken cancellationToken
        )
        {
            return ValueTask.FromResult(true);
        }
    }

    private sealed class NoOpDiagnosticReporter : IEventSubChannelDiagnosticReporter
    {
        public void Report(EventSubChannelDiagnosticReport report) { }
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
