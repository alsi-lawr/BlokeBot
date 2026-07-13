using System.Collections.Immutable;
using System.Net;
using System.Text;
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
        var operations = new TwitchEventSubChannelOperations(
            Settings(),
            new UnusedAccountProvider(),
            CreateResolver(factory),
            new EventSubClient(factory),
            new UnusedChatSender(),
            new UnusedLifecycleNotifier()
        );

        var exception = await Should.ThrowAsync<ChatIdentityResolutionException.MissingChannel>(
            () =>
                operations
                    .CreateSubscriptionAsync(
                        "private-channel-login",
                        new TwitchBotAccount("bot", "access-token"),
                        "session-id",
                        CancellationToken.None
                    )
                    .AsTask()
        );
        var failure = TwitchEventSubChannelFailureClassifier.Classify(
            exception,
            TwitchEventSubChannelPhase.SubscriptionSetup,
            CancellationToken.None
        );

        failure.Classification.ShouldBe(TwitchEventSubChannelFailureClassification.Terminal);
        factory.EventSubRequestCount.ShouldBe(0);
        exception.ToString().ShouldNotContain("private-channel-login");
        exception.ToString().ShouldNotContain("access-token");
    }

    [Test]
    public async Task MissingBot_PreparingPublicChat_IsTerminalWithoutTokenOrSendRequest()
    {
        var factory = new IdentityHttpClientFactory(
            """{"data":[{"id":"channel-id","login":"private-channel-login"}]}"""
        );
        var identity = Identity();
        var transport = new TwitchHelixPublicChatTransport(
            new TwitchAppAccessTokenProvider(factory, identity),
            new StaticAccountProvider(new TwitchBotAccount("private-bot-login", "access-token")),
            identity,
            CreateResolver(factory),
            new ChatClient(factory),
            NullLogger<TwitchHelixPublicChatTransport>.Instance
        );

        var result = await transport.PrepareAsync(
            Message("private-channel-login"),
            CancellationToken.None
        );

        var unexpected = result.ShouldBeOfType<PublicChatPreparationOutcome.Unexpected>();
        unexpected.Cause.ShouldBeOfType<ChatIdentityResolutionException.MissingBot>();
        PublicChatDeliveryClassifier
            .MapPreparationFailure(unexpected)
            .ShouldBeOfType<PublicChatDeliveryOutcome.Unexpected>();
        factory.AppTokenRequestCount.ShouldBe(0);
        factory.ChatRequestCount.ShouldBe(0);
        unexpected.ToString().ShouldNotContain("private-channel-login");
        unexpected.ToString().ShouldNotContain("private-bot-login");
        unexpected.ToString().ShouldNotContain("access-token");
    }

    private static ChatIdentityResolver CreateResolver(IHttpClientFactory factory)
    {
        return new(Identity(), new HelixClient(factory));
    }

    private static TwitchBotSettings Settings()
    {
        return TwitchBotSettings.FromOptions(
            new TwitchBotOptions
            {
                Identity = new TwitchBotIdentityOptions
                {
                    BotUsername = "bot",
                    ClientId = "client-id",
                    ClientSecret = "client-secret",
                    RedirectUri = "https://localhost/callback",
                    Scopes = ["chat:read"],
                    TokenCachePath = "tokens.json",
                },
            }
        );
    }

    private static TwitchBotIdentity Identity()
    {
        return new TwitchBotIdentity
        {
            BotUsername = "bot",
            ClientId = "client-id",
            ClientSecret = "client-secret",
            RedirectUri = "https://localhost/callback",
            Scopes = ImmutableArray.Create("chat:read"),
            TokenCachePath = "tokens.json",
        };
    }

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

        internal int AppTokenRequestCount => _handler.AppTokenRequestCount;

        internal int ChatRequestCount => _handler.ChatRequestCount;

        internal string? LastAuthorization => _handler.LastAuthorization;

        internal string? LastClientId => _handler.LastClientId;

        internal string LastQuery => _handler.LastQuery;

        public HttpClient CreateClient(string name)
        {
            return new(_handler, disposeHandler: false);
        }

        private sealed class Handler(string usersJson) : HttpMessageHandler
        {
            internal int UserRequestCount { get; private set; }

            internal int EventSubRequestCount { get; private set; }

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
                    "/helix/eventsub/subscriptions" => Task.FromResult(EventSubResponse()),
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

            private HttpResponseMessage EventSubResponse()
            {
                EventSubRequestCount++;
                return JsonResponse("""{"data":[{"id":"subscription-id"}]}""");
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

            private static HttpResponseMessage JsonResponse(string json)
            {
                return new(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                };
            }
        }
    }

    private sealed class StaticAccountProvider(TwitchBotAccount account) : ITwitchBotAccountProvider
    {
        public ValueTask<TwitchBotAccount> GetBotAccountAsync(
            string channelLogin,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(account);
        }
    }

    private sealed class UnusedAccountProvider : ITwitchBotAccountProvider
    {
        public ValueTask<TwitchBotAccount> GetBotAccountAsync(
            string channelLogin,
            CancellationToken cancellationToken
        )
        {
            throw new InvalidOperationException("Account lookup was not expected.");
        }
    }

    private sealed class UnusedChatSender : ITwitchChatMessageSender
    {
        public Task SendAsync(
            string channel,
            string message,
            PublicChatDeliveryDeadline deadline,
            CancellationToken cancellationToken
        )
        {
            throw new InvalidOperationException("Chat delivery was not expected.");
        }
    }

    private sealed class UnusedLifecycleNotifier : ITwitchBotChannelLifecycleNotifier
    {
        public Task ChannelStartedAsync(string channel, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Channel startup was not expected.");
        }

        public Task ChannelStoppedAsync(string channel, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Channel stop was not expected.");
        }
    }
}
