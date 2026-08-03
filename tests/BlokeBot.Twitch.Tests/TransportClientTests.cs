using System.Net;
using System.Text;
using System.Text.Json;
using Shouldly;

namespace BlokeBot.Twitch.Tests;

public sealed class TransportClientTests
{
    [Test]
    public async Task EventSubSubscription_Creating_UsesSignedWebhookTransport()
    {
        var factory = new ScriptedHttpClientFactory();
        factory.Respond(
            static async (request, cancellationToken) =>
            {
                AssertContext(request, HttpMethod.Post, "/helix/eventsub/subscriptions");
                using var payload = JsonDocument.Parse(
                    await request.Content!.ReadAsStringAsync(cancellationToken)
                );
                var transport = payload.RootElement.GetProperty("transport");
                transport.GetProperty("method").GetString().ShouldBe("webhook");
                transport
                    .GetProperty("callback")
                    .GetString()
                    .ShouldBe("https://bot.blokebot.com/eventsub/twitch");
                transport.GetProperty("secret").GetString().ShouldBe("webhook-secret");
                return JsonResponse("""{ "data": [{ "id": "subscription-1" }] }""");
            }
        );
        var client = CreateEventSubClient(factory);

        var id = await client.CreateAsync(
            "client-id",
            new EventSubSubscriptionRequest(
                "channel.chat.message",
                "1",
                new Dictionary<string, string>
                {
                    ["broadcaster_user_id"] = "channel-id",
                    ["user_id"] = "bot-id",
                }
            ),
            CancellationToken.None
        );

        id.ShouldBe("subscription-1");
    }

    [Test]
    public async Task EventSubSubscription_Creating_WaitsForCallbackVerification()
    {
        var factory = new ScriptedHttpClientFactory();
        factory.Respond(
            static (_, _) => Task.FromResult(JsonResponse("""{"data":[{"id":"subscription-1"}]}"""))
        );
        var verification = new BlockingVerification();
        var client = CreateEventSubClient(factory, verification);

        var creation = client.CreateAsync(
            "client-id",
            new EventSubSubscriptionRequest(
                "channel.chat.message",
                "1",
                new Dictionary<string, string> { ["broadcaster_user_id"] = "channel-id" }
            ),
            CancellationToken.None
        );
        await verification.WaitStarted.WaitAsync(TimeSpan.FromSeconds(2));

        creation.IsCompleted.ShouldBeFalse();
        verification.Confirm("subscription-1");

        (await creation).ShouldBe("subscription-1");
    }

    [Test]
    public async Task EventSubSubscription_Listing_MapsWebhookTransportAndPagination()
    {
        var factory = new ScriptedHttpClientFactory();
        factory.Respond(
            static (_, _) =>
                Task.FromResult(
                    JsonResponse(
                        """
                        {
                          "data": [{
                            "id": "subscription-1",
                            "status": "enabled",
                            "type": "channel.chat.message",
                            "version": "1",
                            "condition": {"broadcaster_user_id":"channel-id"},
                            "transport": {"method":"webhook", "callback":"https://bot.blokebot.com/eventsub/twitch"}
                          }],
                          "pagination": {"cursor":"next-page"}
                        }
                        """
                    )
                )
        );
        var client = CreateEventSubClient(factory);

        var inventory = await client.ListSubscriptionsAsync(
            "client-id",
            null,
            CancellationToken.None
        );

        inventory.Cursor.ShouldBe("next-page");
        var subscription = inventory.Subscriptions.ShouldHaveSingleItem();
        subscription.Method.ShouldBe("webhook");
        subscription.Callback.ShouldBe("https://bot.blokebot.com/eventsub/twitch");
    }

    [Test]
    public async Task OwnedSubscriptionHealth_ListsOnlyEnabledExactCallbackWebhookIds()
    {
        var factory = new ScriptedHttpClientFactory();
        factory.Respond(
            static (_, _) =>
                Task.FromResult(
                    JsonResponse(
                        """
                        {
                          "data": [
                            {
                              "id": "healthy", "status": "enabled",
                              "type": "channel.chat.message", "version": "1", "condition": {},
                              "transport": {"method":"webhook","callback":"https://bot.blokebot.com/eventsub/twitch"}
                            },
                            {
                              "id": "revoked", "status": "authorization_revoked",
                              "type": "channel.poll.begin", "version": "1", "condition": {},
                              "transport": {"method":"webhook","callback":"https://bot.blokebot.com/eventsub/twitch"}
                            },
                            {
                              "id": "failed", "status": "notification_failures_exceeded",
                              "type": "channel.prediction.begin", "version": "1", "condition": {},
                              "transport": {"method":"webhook","callback":"https://bot.blokebot.com/eventsub/twitch"}
                            },
                            {
                              "id": "disabled", "status": "disabled",
                              "type": "channel.chat.message", "version": "1", "condition": {},
                              "transport": {"method":"webhook","callback":"https://bot.blokebot.com/eventsub/twitch"}
                            },
                            {
                              "id": "pending", "status": "webhook_callback_verification_pending",
                              "type": "channel.chat.message", "version": "1", "condition": {},
                              "transport": {"method":"webhook","callback":"https://bot.blokebot.com/eventsub/twitch"}
                            },
                            {
                              "id": "sibling", "status": "enabled",
                              "type": "channel.chat.message", "version": "1", "condition": {},
                              "transport": {"method":"webhook","callback":"https://sibling.invalid/eventsub"}
                            },
                            {
                              "id": "socket", "status": "enabled",
                              "type": "channel.chat.message", "version": "1", "condition": {},
                              "transport": {"method":"websocket","callback":"https://bot.blokebot.com/eventsub/twitch"}
                            }
                          ],
                          "pagination": {}
                        }
                        """
                    )
                )
        );
        var client = CreateEventSubClient(factory);

        var healthyIds = await client.ListEnabledOwnedIdsAsync("client-id", CancellationToken.None);

        healthyIds.ShouldBe(new HashSet<string>(StringComparer.Ordinal) { "healthy" });
    }

    [Test]
    public async Task ExistingSubscription_Deleting_SendsAuthenticatedDelete()
    {
        var factory = new ScriptedHttpClientFactory();
        factory.Respond(
            static (request, _) =>
            {
                AssertContext(request, HttpMethod.Delete, "/helix/eventsub/subscriptions");
                request.RequestUri!.Query.ShouldBe("?id=subscription%2Fid");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            }
        );
        var client = CreateEventSubClient(factory);

        await client.DeleteAsync("client-id", "subscription/id", CancellationToken.None);

        factory.RequestCount.ShouldBe(1);
    }

    [Test]
    public async Task MissingSubscription_Deleting_TreatsNotFoundAsDeleted()
    {
        var factory = RespondingWith(HttpStatusCode.NotFound);
        var client = CreateEventSubClient(factory);

        await client.DeleteAsync("client-id", "missing", CancellationToken.None);

        factory.RequestCount.ShouldBe(1);
    }

    [Test]
    public async Task StartupReset_PaginatesThenDeletesOnlyExactCallbackWebhooksBeforeCreation()
    {
        var factory = new ScriptedHttpClientFactory();
        factory.Respond(
            static (request, _) =>
            {
                AssertContext(request, HttpMethod.Get, "/helix/eventsub/subscriptions");
                request.RequestUri!.Query.ShouldBeEmpty();
                return Task.FromResult(
                    JsonResponse(
                        """
                        {
                          "data": [
                            {
                              "id": "owned-1", "status": "enabled",
                              "type": "channel.chat.message", "version": "1", "condition": {},
                              "transport": {"method":"webhook","callback":"https://bot.blokebot.com/eventsub/twitch"}
                            },
                            {
                              "id": "sibling", "status": "enabled",
                              "type": "channel.chat.message", "version": "1", "condition": {},
                              "transport": {"method":"webhook","callback":"https://sibling.invalid/eventsub"}
                            },
                            {
                              "id": "socket", "status": "enabled",
                              "type": "channel.chat.message", "version": "1", "condition": {},
                              "transport": {"method":"websocket","callback":"https://bot.blokebot.com/eventsub/twitch"}
                            }
                          ],
                          "pagination": {"cursor":"page-2"}
                        }
                        """
                    )
                );
            }
        );
        factory.Respond(
            static (request, _) =>
            {
                AssertContext(request, HttpMethod.Get, "/helix/eventsub/subscriptions");
                request.RequestUri!.Query.ShouldBe("?after=page-2");
                return Task.FromResult(
                    JsonResponse(
                        """
                        {
                          "data": [
                            {
                              "id": "conduit", "status": "enabled",
                              "type": "channel.chat.message", "version": "1", "condition": {},
                              "transport": {"method":"conduit","callback":"https://bot.blokebot.com/eventsub/twitch"}
                            },
                            {
                              "id": "owned-2", "status": "revoked",
                              "type": "channel.poll.begin", "version": "1", "condition": {},
                              "transport": {"method":"webhook","callback":"https://bot.blokebot.com/eventsub/twitch"}
                            }
                          ],
                          "pagination": {}
                        }
                        """
                    )
                );
            }
        );
        foreach (var expectedId in new[] { "owned-1", "owned-2" })
        {
            factory.Respond(
                (request, _) =>
                {
                    AssertContext(request, HttpMethod.Delete, "/helix/eventsub/subscriptions");
                    request.RequestUri!.Query.ShouldBe($"?id={expectedId}");
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
                }
            );
        }
        factory.Respond(
            static (request, _) =>
            {
                AssertContext(request, HttpMethod.Post, "/helix/eventsub/subscriptions");
                return Task.FromResult(JsonResponse("""{"data":[{"id":"fresh-subscription"}]}"""));
            }
        );
        var client = CreateEventSubClient(factory);

        await client.ResetAsync("client-id", CancellationToken.None);
        var created = await client.CreateAsync(
            "client-id",
            new EventSubSubscriptionRequest(
                "channel.chat.message",
                "1",
                new Dictionary<string, string> { ["broadcaster_user_id"] = "channel-id" }
            ),
            CancellationToken.None
        );

        created.ShouldBe("fresh-subscription");
        factory.RequestCount.ShouldBe(5);
    }

    [Test]
    public async Task StartupReset_DeleteFailureStopsBeforeFreshCreation()
    {
        var factory = new ScriptedHttpClientFactory();
        factory.Respond(
            static (_, _) =>
                Task.FromResult(
                    JsonResponse(
                        """
                        {
                          "data": [{
                            "id": "owned", "status": "enabled",
                            "type": "channel.chat.message", "version": "1", "condition": {},
                            "transport": {"method":"webhook","callback":"https://bot.blokebot.com/eventsub/twitch"}
                          }],
                          "pagination": {}
                        }
                        """
                    )
                )
        );
        factory.Respond(
            static (_, _) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError))
        );
        var client = CreateEventSubClient(factory);

        _ = await Should.ThrowAsync<HttpRequestException>(() =>
            client.ResetAsync("client-id", CancellationToken.None)
        );

        factory.RequestCount.ShouldBe(2);
    }

    [Test]
    public async Task PublicChatMessage_Sending_MapsTypedProviderResult()
    {
        var factory = new ScriptedHttpClientFactory();
        factory.Respond(
            static async (request, cancellationToken) =>
            {
                AssertContext(request, HttpMethod.Post, "/helix/chat/messages");
                using var payload = JsonDocument.Parse(
                    await request.Content!.ReadAsStringAsync(cancellationToken)
                );
                payload
                    .RootElement.GetProperty("broadcaster_id")
                    .GetString()
                    .ShouldBe("channel-id");
                payload.RootElement.GetProperty("sender_id").GetString().ShouldBe("bot-id");
                payload.RootElement.GetProperty("message").GetString().ShouldBe("hello chat");
                return JsonResponse(
                    """
                    {
                      "data": [
                        {
                          "message_id": "message-id",
                          "is_sent": false,
                          "drop_reason": {"code":"followers_only","message":"not allowed"}
                        }
                      ]
                    }
                    """
                );
            }
        );
        var client = new ChatClient(factory, global::BlokeBot.Twitch.TwitchEndpointPolicy.Default);

        var result = await client.SendMessageAsync(
            Context(),
            "channel-id",
            "bot-id",
            "hello chat",
            CancellationToken.None
        );

        result.IsSent.ShouldBeFalse();
        result.MessageId.ShouldBe("message-id");
        result.DropReason!.Code.ShouldBe("followers_only");
        result.DropReason.Message.ShouldBe("not allowed");
        result.ToString().ShouldNotContain("message-id");
        result.ToString().ShouldNotContain("not allowed");
    }

    [Test]
    public async Task WhisperAccepted_Sending_MapsNoContentWithoutBody()
    {
        var factory = new ScriptedHttpClientFactory();
        factory.Respond(
            static async (request, cancellationToken) =>
            {
                AssertContext(request, HttpMethod.Post, "/helix/whispers");
                request.RequestUri!.Query.ShouldContain("from_user_id=sender-id");
                request.RequestUri.Query.ShouldContain("to_user_id=recipient-id");
                using var payload = JsonDocument.Parse(
                    await request.Content!.ReadAsStringAsync(cancellationToken)
                );
                payload.RootElement.GetProperty("message").GetString().ShouldBe("private message");
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
        );
        var client = new WhisperClient(
            factory,
            global::BlokeBot.Twitch.TwitchEndpointPolicy.Default
        );

        var result = await client.SendAsync(
            Context(),
            "sender-id",
            "recipient-id",
            "private message",
            CancellationToken.None
        );

        result.Status.ShouldBe(WhisperSendStatus.Accepted);
        result.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        result.ResponseBody.ShouldBeNull();
    }

    [Test]
    public async Task WhisperRateLimited_Sending_PreservesBoundedResponseBody()
    {
        var responseBody = new string('x', 1005);
        var factory = new ScriptedHttpClientFactory();
        factory.Respond(
            (_, _) =>
                Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                    {
                        Content = new StringContent(
                            responseBody,
                            Encoding.UTF8,
                            "application/json"
                        ),
                    }
                )
        );
        var client = new WhisperClient(
            factory,
            global::BlokeBot.Twitch.TwitchEndpointPolicy.Default
        );

        var result = await client.SendAsync(
            Context(),
            "sender-id",
            "recipient-id",
            "private message",
            CancellationToken.None
        );

        result.Status.ShouldBe(WhisperSendStatus.RateLimited);
        result.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        result.ResponseBody.ShouldBe(new string('x', 1000));
        result.ToString().ShouldNotContain(new string('x', 100));
    }

    [Test]
    public async Task WhisperRejected_Sending_PreservesStatusAndResponseBody()
    {
        var factory = new ScriptedHttpClientFactory();
        factory.Respond(
            static (_, _) =>
                Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.BadRequest)
                    {
                        Content = new StringContent(
                            """{"error":"Bad Request"}""",
                            Encoding.UTF8,
                            "application/json"
                        ),
                    }
                )
        );
        var client = new WhisperClient(
            factory,
            global::BlokeBot.Twitch.TwitchEndpointPolicy.Default
        );

        var result = await client.SendAsync(
            Context(),
            "sender-id",
            "recipient-id",
            "private message",
            CancellationToken.None
        );

        result.Status.ShouldBe(WhisperSendStatus.Rejected);
        result.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        result.ResponseBody.ShouldBe("""{"error":"Bad Request"}""");
        result.ToString().ShouldNotContain("Bad Request");
    }

    [Test]
    public async Task CancelledWhisper_Sending_PreservesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var factory = new ScriptedHttpClientFactory();
        factory.Respond(
            (_, cancellationToken) => Task.FromCanceled<HttpResponseMessage>(cancellationToken)
        );
        var client = new WhisperClient(
            factory,
            global::BlokeBot.Twitch.TwitchEndpointPolicy.Default
        );

        _ = await Should.ThrowAsync<OperationCanceledException>(() =>
            client.SendAsync(
                Context(),
                "sender-id",
                "recipient-id",
                "private message",
                cancellation.Token
            )
        );
    }

    [Test]
    public async Task NativeAnnouncement_Sending_UsesActiveBotModeratorIdentityAndSelectedColor()
    {
        var factory = new ScriptedHttpClientFactory();
        factory.Respond(
            static async (request, cancellationToken) =>
            {
                AssertContext(request, HttpMethod.Post, "/helix/chat/announcements");
                request.RequestUri!.Query.ShouldContain("broadcaster_id=channel-id");
                request.RequestUri.Query.ShouldContain("moderator_id=validated-bot-subject");
                using var payload = JsonDocument.Parse(
                    await request.Content!.ReadAsStringAsync(cancellationToken)
                );
                payload
                    .RootElement.GetProperty("message")
                    .GetString()
                    .ShouldBe("Native announcement");
                payload.RootElement.GetProperty("color").GetString().ShouldBe("purple");
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
        );
        var client = new ChatAnnouncementClient(
            factory,
            global::BlokeBot.Twitch.TwitchEndpointPolicy.Default
        );

        var result = await client.SendAsync(
            Context(),
            "channel-id",
            "validated-bot-subject",
            "Native announcement",
            TwitchAnnouncementColor.Purple,
            CancellationToken.None
        );

        _ = result.ShouldBeOfType<ChatAnnouncementSendResult.Sent>();
    }

    [Test]
    [Arguments(HttpStatusCode.BadRequest, typeof(ChatAnnouncementSendResult.Invalid))]
    [Arguments(HttpStatusCode.Unauthorized, typeof(ChatAnnouncementSendResult.PermissionDenied))]
    [Arguments(HttpStatusCode.Forbidden, typeof(ChatAnnouncementSendResult.PermissionDenied))]
    [Arguments(HttpStatusCode.TooManyRequests, typeof(ChatAnnouncementSendResult.RateLimited))]
    [Arguments(HttpStatusCode.InternalServerError, typeof(ChatAnnouncementSendResult.Ambiguous))]
    [Arguments(HttpStatusCode.NotFound, typeof(ChatAnnouncementSendResult.Unexpected))]
    public async Task NativeAnnouncement_ResponseStatus_MapsTypedResult(
        HttpStatusCode statusCode,
        Type expectedResultType
    )
    {
        var client = new ChatAnnouncementClient(
            RespondingWith(statusCode),
            global::BlokeBot.Twitch.TwitchEndpointPolicy.Default
        );

        var result = await client.SendAsync(
            Context(),
            "channel-id",
            "validated-bot-subject",
            "Native announcement",
            TwitchAnnouncementColor.Primary,
            CancellationToken.None
        );

        result.GetType().ShouldBe(expectedResultType);
    }

    [Test]
    public async Task NativeAnnouncement_InvalidLength_DoesNotSend()
    {
        var factory = new ScriptedHttpClientFactory();
        var client = new ChatAnnouncementClient(
            factory,
            global::BlokeBot.Twitch.TwitchEndpointPolicy.Default
        );

        var result = await client.SendAsync(
            Context(),
            "channel-id",
            "validated-bot-subject",
            new string('x', 501),
            TwitchAnnouncementColor.Primary,
            CancellationToken.None
        );

        _ = result.ShouldBeOfType<ChatAnnouncementSendResult.Invalid>();
        factory.RequestCount.ShouldBe(0);
    }

    [Test]
    public async Task NativeAnnouncement_UnsupportedColor_DoesNotSend()
    {
        var factory = new ScriptedHttpClientFactory();
        var client = new ChatAnnouncementClient(
            factory,
            global::BlokeBot.Twitch.TwitchEndpointPolicy.Default
        );

        var result = await client.SendAsync(
            Context(),
            "channel-id",
            "validated-bot-subject",
            "Native announcement",
            (TwitchAnnouncementColor)99,
            CancellationToken.None
        );

        _ = result.ShouldBeOfType<ChatAnnouncementSendResult.Invalid>();
        factory.RequestCount.ShouldBe(0);
    }

    [Test]
    public async Task NativeAnnouncement_TransportFailure_RemainsAmbiguous()
    {
        var factory = new ScriptedHttpClientFactory();
        factory.Respond(static (_, _) => throw new HttpRequestException("connection lost"));
        var client = new ChatAnnouncementClient(
            factory,
            global::BlokeBot.Twitch.TwitchEndpointPolicy.Default
        );

        var result = await client.SendAsync(
            Context(),
            "channel-id",
            "validated-bot-subject",
            "Native announcement",
            TwitchAnnouncementColor.Primary,
            CancellationToken.None
        );

        _ = result.ShouldBeOfType<ChatAnnouncementSendResult.Ambiguous>();
    }

    [Test]
    [Arguments(null)]
    [Arguments(30)]
    [Arguments(1800)]
    public async Task ChatMessagePinning_UsesExactMessageAndNativeDuration(int? durationSeconds)
    {
        var factory = new ScriptedHttpClientFactory();
        factory.Respond(
            (request, _) =>
            {
                AssertContext(request, HttpMethod.Put, "/helix/chat/pins");
                request.RequestUri!.Query.ShouldContain("broadcaster_id=channel-id");
                request.RequestUri.Query.ShouldContain("moderator_id=bot-id");
                request.RequestUri.Query.ShouldContain("message_id=exact-message-id");
                if (durationSeconds is { } seconds)
                {
                    request.RequestUri.Query.ShouldContain($"duration_seconds={seconds}");
                }
                else
                {
                    request.RequestUri.Query.ShouldNotContain("duration_seconds");
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            }
        );
        var client = new ChatPinClient(
            factory,
            global::BlokeBot.Twitch.TwitchEndpointPolicy.Default
        );

        var result = await client.PinAsync(
            Context(),
            "channel-id",
            "bot-id",
            "exact-message-id",
            durationSeconds,
            CancellationToken.None
        );

        _ = result.ShouldBeOfType<ChatPinMutationResult.Succeeded>();
    }

    [Test]
    [Arguments(HttpStatusCode.Conflict, typeof(ChatPinMutationResult.Conflict))]
    [Arguments(HttpStatusCode.Forbidden, typeof(ChatPinMutationResult.PermissionDenied))]
    [Arguments(HttpStatusCode.TooManyRequests, typeof(ChatPinMutationResult.RateLimited))]
    public async Task ChatMessagePinning_RequiredFailuresRemainTyped(
        HttpStatusCode statusCode,
        Type expectedType
    )
    {
        var client = new ChatPinClient(
            RespondingWith(statusCode),
            global::BlokeBot.Twitch.TwitchEndpointPolicy.Default
        );

        var result = await client.PinAsync(
            Context(),
            "channel-id",
            "bot-id",
            "message-id",
            300,
            CancellationToken.None
        );

        result.GetType().ShouldBe(expectedType);
    }

    private static EventSubClient CreateEventSubClient(
        ScriptedHttpClientFactory factory,
        IEventSubSubscriptionVerification? verification = null
    ) =>
        new(
            factory,
            global::BlokeBot.Twitch.TwitchEndpointPolicy.Default,
            new EventSubWebhookOptions
            {
                CallbackUri = new Uri("https://bot.blokebot.com/eventsub/twitch"),
                Secret = "webhook-secret",
            },
            new StaticAppAccessTokenProvider(),
            verification ?? new ImmediateVerification()
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

    private sealed class BlockingVerification : IEventSubSubscriptionVerification
    {
        private readonly TaskCompletionSource _confirmed = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly TaskCompletionSource _waitStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        internal Task WaitStarted => _waitStarted.Task;

        public async Task WaitAsync(string subscriptionId, CancellationToken cancellationToken)
        {
            _ = _waitStarted.TrySetResult();
            await _confirmed.Task.WaitAsync(cancellationToken);
        }

        public void Confirm(string subscriptionId) => _confirmed.TrySetResult();
    }

    private static void AssertContext(
        HttpRequestMessage request,
        HttpMethod method,
        string absolutePath
    )
    {
        request.Method.ShouldBe(method);
        request.RequestUri!.AbsolutePath.ShouldBe(absolutePath);
        request.Headers.Authorization!.Scheme.ShouldBe("Bearer");
        request.Headers.Authorization.Parameter.ShouldBe(
            absolutePath.StartsWith("/helix/eventsub/", StringComparison.Ordinal)
                ? "app-access-token"
                : "access-token"
        );
        request.Headers.GetValues("Client-Id").Single().ShouldBe("client-id");
    }

    private static HelixRequestContext Context() => new("client-id", "access-token");

    private static ScriptedHttpClientFactory RespondingWith(HttpStatusCode statusCode)
    {
        var factory = new ScriptedHttpClientFactory();
        factory.Respond((_, _) => Task.FromResult(new HttpResponseMessage(statusCode)));
        return factory;
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    private sealed class ScriptedHttpClientFactory : IHttpClientFactory
    {
        private readonly Queue<
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>
        > _responses = new();

        internal int RequestCount { get; private set; }

        internal void Respond(
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response
        ) => _responses.Enqueue(response);

        public HttpClient CreateClient(string name) =>
            new(new Handler(this), disposeHandler: false);

        private sealed class Handler(ScriptedHttpClientFactory owner) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken
            )
            {
                owner.RequestCount++;
                owner._responses.Count.ShouldBeGreaterThan(0);
                return owner._responses.Dequeue()(request, cancellationToken);
            }
        }
    }
}
