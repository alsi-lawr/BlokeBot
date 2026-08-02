using System.Collections.Immutable;
using System.Net;
using System.Text;
using System.Text.Json;
using BlokeBot.Functional;
using BlokeBot.Twitch.Auth;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Twitch.Runtime.Tests;

public sealed class ChatIdentityResolverTests
{
    [Test]
    public async Task ChannelAndBotUsers_Resolving_ReturnsResolvedIdentities()
    {
        var factory = new IdentityHttpClientFactory(
            """
            {"data":[{"id":"channel-id","login":"channel"},{"id":"bot-id","login":"bot"}]}
            """
        );
        var resolver = CreateResolver(factory);

        var result = await resolver.ResolveAsync(
            "Channel",
            "Bot",
            "access-token",
            CancellationToken.None
        );

        var resolved = result.Match(
            identity => identity,
            _ => throw new InvalidOperationException("Expected resolved chat identities."),
            _ => throw new InvalidOperationException("Expected resolved chat identities.")
        );
        resolved.BroadcasterId.ShouldBe("channel-id");
        resolved.BotUserId.ShouldBe("bot-id");
        factory.UserRequestCount.ShouldBe(1);
        factory.LastAuthorization.ShouldBe("Bearer access-token");
        factory.LastClientId.ShouldBe("client-id");
        factory.LastQuery.ShouldContain("login=channel");
        factory.LastQuery.ShouldContain("login=bot");
    }

    [Test]
    public async Task BotUserWithoutChannel_Resolving_ReturnsMissingChannel()
    {
        var factory = new IdentityHttpClientFactory("""{"data":[{"id":"bot-id","login":"bot"}]}""");
        var resolver = CreateResolver(factory);

        var result = await resolver.ResolveAsync(
            "channel",
            "bot",
            "access-token",
            CancellationToken.None
        );

        result.ShouldBeOfType<ChatIdentityResolution.MissingChannel>();
    }

    [Test]
    public async Task ChannelUserWithoutBot_Resolving_ReturnsMissingBot()
    {
        var factory = new IdentityHttpClientFactory(
            """{"data":[{"id":"channel-id","login":"channel"}]}"""
        );
        var resolver = CreateResolver(factory);

        var result = await resolver.ResolveAsync(
            "channel",
            "bot",
            "access-token",
            CancellationToken.None
        );

        result.ShouldBeOfType<ChatIdentityResolution.MissingBot>();
    }

    [Test]
    public async Task MissingChannel_CreatingEventSubSubscription_IsTerminalWithoutCreateRequest()
    {
        var factory = new IdentityHttpClientFactory("""{"data":[{"id":"bot-id","login":"bot"}]}""");
        var operations = new EventSubChannelOperations(
            Settings(),
            new UnusedAccountProvider(),
            CreateResolver(factory),
            new EventSubClient(factory, global::BlokeBot.Twitch.TwitchEndpointPolicy.Default),
            null!,
            new UnusedChatSender(),
            new UnusedLifecycleNotifier(),
            new EnabledNativeTwitchFeatureStateProvider()
        );

        var outcome = await operations.CreateSubscriptionAsync(
            "private-channel-login",
            EventSubAuthorizationContext.ConfiguredBotAuthority,
            new BotAccount("bot", "access-token"),
            "session-id",
            CancellationToken.None
        );

        outcome.ShouldBeOfType<EventSubSubscriptionSetupOutcome.MissingChannel>();
        factory.EventSubRequestCount.ShouldBe(0);
        outcome.ToString().ShouldNotContain("private-channel-login");
        outcome.ToString().ShouldNotContain("access-token");
    }

    [Test]
    public async Task MissingBot_CreatingEventSubSubscription_IsTerminalWithoutCreateRequest()
    {
        var factory = new IdentityHttpClientFactory(
            """{"data":[{"id":"channel-id","login":"private-channel-login"}]}"""
        );
        var operations = new EventSubChannelOperations(
            Settings(),
            new UnusedAccountProvider(),
            CreateResolver(factory),
            new EventSubClient(factory, global::BlokeBot.Twitch.TwitchEndpointPolicy.Default),
            null!,
            new UnusedChatSender(),
            new UnusedLifecycleNotifier(),
            new EnabledNativeTwitchFeatureStateProvider()
        );

        var outcome = await operations.CreateSubscriptionAsync(
            "private-channel-login",
            EventSubAuthorizationContext.ConfiguredBotAuthority,
            new BotAccount("private-bot-login", "access-token"),
            "session-id",
            CancellationToken.None
        );

        outcome.ShouldBeOfType<EventSubSubscriptionSetupOutcome.MissingBot>();
        factory.EventSubRequestCount.ShouldBe(0);
        outcome.ToString().ShouldNotContain("private-channel-login");
        outcome.ToString().ShouldNotContain("private-bot-login");
        outcome.ToString().ShouldNotContain("access-token");
    }

    [Test]
    public async Task MissingChannel_PreparingPublicChat_IsTerminalWithoutTokenOrSendRequest()
    {
        var factory = new IdentityHttpClientFactory(
            """{"data":[{"id":"bot-id","login":"private-bot-login"}]}"""
        );
        var identity = Identity();
        var transport = new HelixPublicChatTransport(
            new AppAccessTokenProvider(
                factory,
                identity,
                global::BlokeBot.Twitch.TwitchEndpointPolicy.Default
            ),
            new StaticAccountProvider(new BotAccount("private-bot-login", "access-token")),
            identity,
            CreateResolver(factory),
            new ChatClient(factory, global::BlokeBot.Twitch.TwitchEndpointPolicy.Default),
            NullLogger<HelixPublicChatTransport>.Instance
        );

        var result = await transport.PrepareAsync(
            Message("private-channel-login"),
            CancellationToken.None
        );

        var missingChannel = result.ShouldBeOfType<PublicChatPreparationOutcome.MissingChannel>();
        PublicChatDeliveryClassifier
            .MapPreparationFailure(missingChannel)
            .ShouldBeOfType<PublicChatDeliveryOutcome.MissingChannel>();
        factory.AppTokenRequestCount.ShouldBe(0);
        factory.ChatRequestCount.ShouldBe(0);
        missingChannel.ToString().ShouldNotContain("private-channel-login");
        missingChannel.ToString().ShouldNotContain("private-bot-login");
        missingChannel.ToString().ShouldNotContain("access-token");
    }

    [Test]
    public async Task MissingBot_PreparingPublicChat_IsTerminalWithoutTokenOrSendRequest()
    {
        var factory = new IdentityHttpClientFactory(
            """{"data":[{"id":"channel-id","login":"private-channel-login"}]}"""
        );
        var identity = Identity();
        var transport = new HelixPublicChatTransport(
            new AppAccessTokenProvider(
                factory,
                identity,
                global::BlokeBot.Twitch.TwitchEndpointPolicy.Default
            ),
            new StaticAccountProvider(new BotAccount("private-bot-login", "access-token")),
            identity,
            CreateResolver(factory),
            new ChatClient(factory, global::BlokeBot.Twitch.TwitchEndpointPolicy.Default),
            NullLogger<HelixPublicChatTransport>.Instance
        );

        var result = await transport.PrepareAsync(
            Message("private-channel-login"),
            CancellationToken.None
        );

        var missingBot = result.ShouldBeOfType<PublicChatPreparationOutcome.MissingBot>();
        PublicChatDeliveryClassifier
            .MapPreparationFailure(missingBot)
            .ShouldBeOfType<PublicChatDeliveryOutcome.MissingBot>();
        factory.AppTokenRequestCount.ShouldBe(0);
        factory.ChatRequestCount.ShouldBe(0);
        missingBot.ToString().ShouldNotContain("private-channel-login");
        missingBot.ToString().ShouldNotContain("private-bot-login");
        missingBot.ToString().ShouldNotContain("access-token");
    }

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    public async Task PollSubscriptionFailure_AfterBeginOrProgress_CleansUpEveryCreatedSubscription(
        int successfulPollSubscriptions
    )
    {
        var factory = new IdentityHttpClientFactory(
            """{"data":[{"id":"channel-id","login":"channel"}]}"""
        );
        var operations = CreateEventSubOperations(
            factory,
            new ScriptedBroadcasterAccountProvider(
                Result<BotAccount, AccessTokenUnavailableReason>.Success(
                    new BotAccount("channel", "broadcaster-token")
                )
            )
        );
        factory.FailNextEventSubPostAfter(successfulPollSubscriptions);

        var outcome = await operations.CreateSubscriptionAsync(
            "channel",
            EventSubAuthorizationContext.BroadcasterAuthority,
            new BotAccount("channel", "broadcaster-token"),
            "session-id",
            CancellationToken.None
        );

        var partial = outcome.ShouldBeOfType<EventSubSubscriptionSetupOutcome.PartiallyCreated>();
        var subscriptionIds = new[] { partial.Subscription.SubscriptionId }
            .Concat(partial.Subscription.AdditionalSubscriptionIds)
            .ToArray();
        subscriptionIds.Length.ShouldBe(successfulPollSubscriptions);

        var deleted = await operations.DeleteSubscriptionAsync(
            partial.Subscription,
            CancellationToken.None
        );

        deleted.ShouldBeOfType<EventSubSubscriptionDeletionOutcome.Deleted>();
        factory
            .EventSubRequests.Where(request => request.Method == HttpMethod.Delete)
            .Select(request => request.SubscriptionId)
            .ShouldBe(subscriptionIds);
        factory
            .EventSubRequests.Where(request => request.Method == HttpMethod.Delete)
            .Select(request => request.Authorization)
            .Distinct()
            .ShouldBe(["Bearer broadcaster-token"]);
    }

    [Test]
    public async Task IncomingRaidSubscription_Creating_UsesConfiguredBotUserTokenWithoutAppToken()
    {
        var factory = new IdentityHttpClientFactory(
            """{"data":[{"id":"channel-id","login":"channel"}]}"""
        );
        var operations = CreateEventSubOperations(
            factory,
            new ScriptedBroadcasterAccountProvider()
        );

        var outcome = await operations.CreateSubscriptionAsync(
            "channel",
            EventSubAuthorizationContext.ConfiguredBotAuthority,
            new BotAccount("bot", "configured-bot-user-token"),
            "session-id",
            CancellationToken.None,
            EventSubOperationSubscriptionKind.Raids
        );

        var created = outcome.ShouldBeOfType<EventSubSubscriptionSetupOutcome.Created>();
        created.Subscription.Authorization.ShouldBeOfType<EventSubAuthorizationContext.ConfiguredBot>();
        created.Subscription.AccessToken.ShouldBe("configured-bot-user-token");
        factory.AppTokenRequestCount.ShouldBe(0);
        factory
            .EventSubRequests.ShouldHaveSingleItem()
            .ShouldSatisfyAllConditions(
                request => request.Method.ShouldBe(HttpMethod.Post),
                request => request.Type.ShouldBe("channel.raid"),
                request => request.Authorization.ShouldBe("Bearer configured-bot-user-token")
            );
        factory.LastAuthorization.ShouldBe("Bearer configured-bot-user-token");
    }

    [Test]
    public async Task PollSubscriptionGroup_NoGrantPreservesBotGroup_AndRecreateUsesBroadcasterAuthority()
    {
        var factory = new IdentityHttpClientFactory(
            """
            {"data":[{"id":"channel-id","login":"channel"},{"id":"bot-id","login":"bot"}]}
            """
        );
        var broadcaster = new BotAccount("channel", "broadcaster-token");
        var operations = CreateEventSubOperations(
            factory,
            new ScriptedBroadcasterAccountProvider(
                Result<BotAccount, AccessTokenUnavailableReason>.Error(
                    AccessTokenUnavailableReason.BroadcasterAuthorizationUnavailable
                ),
                Result<BotAccount, AccessTokenUnavailableReason>.Success(broadcaster),
                Result<BotAccount, AccessTokenUnavailableReason>.Success(broadcaster),
                Result<BotAccount, AccessTokenUnavailableReason>.Success(broadcaster)
            )
        );
        var configuredBot = new BotAccount("bot", "configured-bot-token");

        var botSetup = await operations.CreateSubscriptionAsync(
            "channel",
            EventSubAuthorizationContext.ConfiguredBotAuthority,
            configuredBot,
            "session-id",
            CancellationToken.None
        );
        var botSubscription = botSetup
            .ShouldBeOfType<EventSubSubscriptionSetupOutcome.Created>()
            .Subscription;
        var shoutoutSetup = await operations.CreateSubscriptionAsync(
            "channel",
            EventSubAuthorizationContext.ConfiguredBotOperationsAuthority,
            configuredBot,
            "session-id",
            CancellationToken.None
        );
        shoutoutSetup.ShouldBeOfType<EventSubSubscriptionSetupOutcome.Created>();
        var unavailable = await operations
            .ResolveAccount("channel", EventSubAuthorizationContext.BroadcasterAuthority)
            .ExecuteAsync(CancellationToken.None);

        unavailable.ShouldBe(
            Result<BotAccount, AccessTokenUnavailableReason>.Error(
                AccessTokenUnavailableReason.BroadcasterAuthorizationUnavailable
            )
        );
        botSubscription.PollSubscriptions.ShouldBeOfType<EventSubOperationSubscriptionState.NotConfigured>();
        factory.EventSubRequestCount.ShouldBe(3);

        var created = await operations.CreateSubscriptionAsync(
            "channel",
            EventSubAuthorizationContext.BroadcasterAuthority,
            await ResolveBroadcasterAsync(operations),
            "session-id",
            CancellationToken.None
        );
        var pollSubscription = created
            .ShouldBeOfType<EventSubSubscriptionSetupOutcome.Created>()
            .Subscription;
        var deleted = await operations.DeleteSubscriptionAsync(
            pollSubscription,
            CancellationToken.None
        );
        var recreated = await operations.CreateSubscriptionAsync(
            "channel",
            EventSubAuthorizationContext.BroadcasterAuthority,
            await ResolveBroadcasterAsync(operations),
            "replacement-session-id",
            CancellationToken.None
        );

        deleted.ShouldBeOfType<EventSubSubscriptionDeletionOutcome.Deleted>();
        recreated.ShouldBeOfType<EventSubSubscriptionSetupOutcome.Created>();
        factory
            .EventSubRequests.Where(request => request.Method == HttpMethod.Post)
            .Take(3)
            .Select(request => request.Type)
            .ShouldBe([
                "channel.chat.message",
                "channel.shoutout.create",
                "channel.shoutout.receive",
            ]);
        factory
            .EventSubRequests.Where(request => request.Type?.StartsWith("channel.poll.") == true)
            .Select(request => request.Authorization)
            .Distinct()
            .ShouldBe(["Bearer broadcaster-token"]);
        factory
            .EventSubRequests.Where(request => request.Method == HttpMethod.Delete)
            .Select(request => request.Authorization)
            .Distinct()
            .ShouldBe(["Bearer broadcaster-token"]);
    }

    [Test]
    public async Task AcceptedStartupMessage_Delivering_ReturnsCompleted()
    {
        var chat = new ScriptedChatSender(new PublicChatSendOutcome.Accepted());
        var operations = StartupOperations(chat);

        var outcome = await operations.DeliverStartupMessageAsync(
            "private-channel-login",
            CancellationToken.None
        );

        outcome.ShouldBeOfType<EventSubStartupDeliveryOutcome.Completed>();
        chat.Messages.ShouldBe(["private startup payload"]);
        chat.Channels.ShouldBe(["private-channel-login"]);
        chat.Deadlines.ShouldHaveSingleItem()
            .ShouldBeOfType<PublicChatDeliveryDeadline.ConfiguredMaximum>();
    }

    [Test]
    public async Task DisabledStartupMessage_InitialAndReconnectDelivery_CompleteWithoutChatAttempt()
    {
        var chat = new ScriptedChatSender(new PublicChatSendOutcome.Accepted());
        var operations = StartupOperations(chat, new StartupChatMessage.Disabled());

        var outcome = await operations.DeliverStartupMessageAsync(
            "private-channel-login",
            CancellationToken.None
        );
        var reconnectOutcome = await operations.DeliverStartupMessageAsync(
            "private-channel-login",
            CancellationToken.None
        );

        outcome.ShouldBeOfType<EventSubStartupDeliveryOutcome.Completed>();
        reconnectOutcome.ShouldBeOfType<EventSubStartupDeliveryOutcome.Completed>();
        chat.Messages.ShouldBeEmpty();
    }

    [Test]
    public async Task RejectedStartupMessage_Delivering_ReturnsTypedRejection()
    {
        var chat = new ScriptedChatSender(new PublicChatSendOutcome.Rejected());
        var operations = StartupOperations(chat);

        var outcome = await operations.DeliverStartupMessageAsync(
            "private-channel-login",
            CancellationToken.None
        );

        outcome.ShouldBeOfType<EventSubStartupDeliveryOutcome.Rejected>();
        chat.Messages.ShouldBe(["private startup payload"]);
        outcome.ToString().ShouldNotContain("private-channel-login");
        outcome.ToString().ShouldNotContain("private startup payload");
    }

    [Test]
    public async Task CallerCancellation_DeliveringStartupMessage_PropagatesWithoutAttempt()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var chat = new ScriptedChatSender(new PublicChatSendOutcome.Accepted());
        var operations = StartupOperations(chat);

        var thrown = await Should.ThrowAsync<OperationCanceledException>(() =>
            operations
                .DeliverStartupMessageAsync("private-channel-login", cancellation.Token)
                .AsTask()
        );

        thrown.CancellationToken.ShouldBe(cancellation.Token);
        chat.Messages.ShouldBeEmpty();
    }

    private static ChatIdentityResolver CreateResolver(IHttpClientFactory factory) =>
        new(
            Identity(),
            new HelixClient(factory, global::BlokeBot.Twitch.TwitchEndpointPolicy.Default)
        );

    private static EventSubChannelOperations CreateEventSubOperations(
        IdentityHttpClientFactory factory,
        IBroadcasterAccountProvider broadcasters
    ) =>
        new EventSubChannelOperations(
            Settings(),
            new UnusedAccountProvider(),
            CreateResolver(factory),
            new EventSubClient(factory, global::BlokeBot.Twitch.TwitchEndpointPolicy.Default),
            null!,
            new UnusedChatSender(),
            new UnusedLifecycleNotifier(),
            new EnabledNativeTwitchFeatureStateProvider(),
            broadcasters
        );

    private static async Task<BotAccount> ResolveBroadcasterAsync(
        EventSubChannelOperations operations
    )
    {
        var result = await operations
            .ResolveAccount("channel", EventSubAuthorizationContext.BroadcasterAuthority)
            .ExecuteAsync(CancellationToken.None);
        return result.Match(
            account => account,
            reason =>
                throw new InvalidOperationException($"Expected broadcaster account: {reason}.")
        );
    }

    private static EventSubChannelOperations StartupOperations(
        IPublicChatMessageSender sender,
        StartupChatMessage? startupMessage = null
    )
    {
        var factory = new IdentityHttpClientFactory("""{"data":[]}""");
        return new(
            Settings("private startup payload"),
            new UnusedAccountProvider(),
            CreateResolver(factory),
            new EventSubClient(factory, global::BlokeBot.Twitch.TwitchEndpointPolicy.Default),
            new StaticStartupMessageProvider(
                startupMessage ?? new StartupChatMessage.Enabled("private startup payload")
            ),
            sender,
            new UnusedLifecycleNotifier(),
            new EnabledNativeTwitchFeatureStateProvider()
        );
    }

    private sealed class StaticStartupMessageProvider(StartupChatMessage message)
        : IStartupChatMessageProvider
    {
        public ValueTask<StartupChatMessage> GetAsync(
            string channel,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(message);
        }
    }

    private static BotSettings Settings(string startupMessage = "") =>
        BotSettings.FromOptions(
            new BotOptions
            {
                Identity = new BotIdentityOptions
                {
                    BotUsername = "bot",
                    ClientId = "client-id",
                    ClientSecret = "client-secret",
                    RedirectUri = "https://localhost/callback",
                    Scopes = ["chat:read"],
                    TokenCachePath = "tokens.json",
                },
                StartupMessage = startupMessage,
            }
        );

    private static BotIdentity Identity() =>
        new BotIdentity
        {
            BotUsername = "bot",
            ClientId = "client-id",
            ClientSecret = "client-secret",
            RedirectUri = "https://localhost/callback",
            Scopes = OAuthAuthorizationScopeSet.Create(["chat:read"]),
            TokenCachePath = "tokens.json",
        };

    private static PublicChatClaimedMessage Message(string channel)
    {
        var now = new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);
        return new PublicChatClaimedMessage
        {
            Id = 1,
            Channel = channel,
            Message = "message",
            EnqueuedAt = now,
            ExpiresAt = now.AddSeconds(30),
            Attempt = 1,
            ClaimToken = new PublicChatClaimToken(
                Guid.Parse("11111111-1111-1111-1111-111111111111")
            ),
            ClaimExpiresAt = now.AddSeconds(10),
            DeduplicationKey = new PublicChatDeduplicationKey("deduplication-key"),
        };
    }

    private sealed class IdentityHttpClientFactory(string usersJson) : IHttpClientFactory
    {
        private readonly Handler _handler = new(usersJson);

        internal int UserRequestCount => _handler.UserRequestCount;

        internal int EventSubRequestCount => _handler.EventSubRequestCount;

        internal IReadOnlyList<EventSubRequest> EventSubRequests => _handler.EventSubRequests;

        internal void FailNextEventSubPostAfter(int successfulPosts) =>
            _handler.FailNextEventSubPostAfter(successfulPosts);

        internal int AppTokenRequestCount => _handler.AppTokenRequestCount;

        internal int ChatRequestCount => _handler.ChatRequestCount;

        internal string? LastAuthorization => _handler.LastAuthorization;

        internal string? LastClientId => _handler.LastClientId;

        internal string LastQuery => _handler.LastQuery;

        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);

        internal sealed record EventSubRequest(
            HttpMethod Method,
            string? Type,
            string? SubscriptionId,
            string? Authorization
        );

        private sealed class Handler(string usersJson) : HttpMessageHandler
        {
            internal int UserRequestCount { get; private set; }

            internal int EventSubRequestCount { get; private set; }

            private int? _failEventSubPostAt;

            internal List<EventSubRequest> EventSubRequests { get; } = [];

            internal void FailNextEventSubPostAfter(int successfulPosts) =>
                _failEventSubPostAt =
                    EventSubRequests.Count(request => request.Method == HttpMethod.Post)
                    + successfulPosts
                    + 1;

            internal int AppTokenRequestCount { get; private set; }

            internal int ChatRequestCount { get; private set; }

            internal string? LastAuthorization { get; private set; }

            internal string? LastClientId { get; private set; }

            internal string LastQuery { get; private set; } = string.Empty;

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken
            )
            {
                cancellationToken.ThrowIfCancellationRequested();
                return request.RequestUri!.AbsolutePath switch
                {
                    "/helix/users" => Task.FromResult(UserResponse(request)),
                    "/helix/eventsub/subscriptions" => Task.FromResult(EventSubResponse(request)),
                    "/helix/chat/messages" => Task.FromResult(ChatResponse()),
                    "/oauth2/token" => Task.FromResult(AppTokenResponse()),
                    _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)),
                };
            }

            private HttpResponseMessage UserResponse(HttpRequestMessage request)
            {
                UserRequestCount++;
                LastAuthorization = request.Headers.Authorization?.ToString();
                LastClientId = request.Headers.GetValues("Client-Id").Single();
                LastQuery = request.RequestUri!.Query;
                return JsonResponse(usersJson);
            }

            private HttpResponseMessage EventSubResponse(HttpRequestMessage request)
            {
                EventSubRequestCount++;
                string? type = null;
                if (request.Content is not null)
                {
                    using var document = JsonDocument.Parse(
                        request.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                    );
                    type = document.RootElement.GetProperty("type").GetString();
                }
                var subscriptionId = request
                    .RequestUri?.Query.TrimStart('?')
                    .Split('&', StringSplitOptions.RemoveEmptyEntries)
                    .Select(parameter => parameter.Split('=', 2))
                    .FirstOrDefault(parameter => parameter[0] == "id")
                    ?.ElementAtOrDefault(1);
                EventSubRequests.Add(
                    new EventSubRequest(
                        request.Method,
                        type,
                        subscriptionId,
                        request.Headers.Authorization?.ToString()
                    )
                );
                if (request.Method == HttpMethod.Delete)
                {
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                }

                return
                    _failEventSubPostAt
                    == EventSubRequests.Count(eventSubRequest =>
                        eventSubRequest.Method == HttpMethod.Post
                    )
                    ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                    : JsonResponse(
                        $$"""{"data":[{"id":"subscription-{{EventSubRequestCount}}"}]}"""
                    );
            }

            private HttpResponseMessage ChatResponse()
            {
                ChatRequestCount++;
                return JsonResponse("""{"data":[{"message_id":"message-id","is_sent":true}]}""");
            }

            private HttpResponseMessage AppTokenResponse()
            {
                AppTokenRequestCount++;
                return JsonResponse(
                    """{"access_token":"app-token","expires_in":3600,"token_type":"bearer"}"""
                );
            }

            private static HttpResponseMessage JsonResponse(string json) =>
                new(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                };
        }
    }

    private sealed class StaticAccountProvider(BotAccount account) : IBotAccountProvider
    {
        public IO<BotAccount, AccessTokenUnavailableReason> GetBotAccount(string channelLogin) =>
            IO<BotAccount, AccessTokenUnavailableReason>.Create(cancellationToken =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ValueTask.FromResult(
                    Result<BotAccount, AccessTokenUnavailableReason>.Success(account)
                );
            });
    }

    private sealed class ScriptedBroadcasterAccountProvider(
        params Result<BotAccount, AccessTokenUnavailableReason>[] results
    ) : IBroadcasterAccountProvider
    {
        private readonly Queue<Result<BotAccount, AccessTokenUnavailableReason>> _results = new(
            results
        );

        public IO<BotAccount, AccessTokenUnavailableReason> GetBroadcasterAccount(
            string channelLogin
        ) =>
            IO<BotAccount, AccessTokenUnavailableReason>.Create(cancellationToken =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ValueTask.FromResult(_results.Dequeue());
            });
    }

    private sealed class UnusedAccountProvider : IBotAccountProvider
    {
        public IO<BotAccount, AccessTokenUnavailableReason> GetBotAccount(string channelLogin) =>
            IO<BotAccount, AccessTokenUnavailableReason>.Create(_ =>
                ValueTask.FromException<Result<BotAccount, AccessTokenUnavailableReason>>(
                    new InvalidOperationException("Account lookup was not expected.")
                )
            );
    }

    private sealed class UnusedChatSender : IPublicChatMessageSender
    {
        public ValueTask<PublicChatSendOutcome> SendAsync(
            string channel,
            string message,
            PublicChatDeliveryDeadline deadline,
            CancellationToken cancellationToken
        ) => throw new InvalidOperationException("Chat delivery was not expected.");
    }

    private sealed class ScriptedChatSender(PublicChatSendOutcome outcome)
        : IPublicChatMessageSender
    {
        internal List<string> Channels { get; } = [];

        internal List<string> Messages { get; } = [];

        internal List<PublicChatDeliveryDeadline> Deadlines { get; } = [];

        public ValueTask<PublicChatSendOutcome> SendAsync(
            string channel,
            string message,
            PublicChatDeliveryDeadline deadline,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            Channels.Add(channel);
            Messages.Add(message);
            Deadlines.Add(deadline);
            return ValueTask.FromResult(outcome);
        }
    }

    private sealed class UnusedLifecycleNotifier : IBotChannelLifecycleNotifier
    {
        public Task ChannelStartedAsync(string channel, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Channel startup was not expected.");

        public Task ChannelStoppedAsync(string channel, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Channel stop was not expected.");
    }
}
