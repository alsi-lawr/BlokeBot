using System.Net;
using System.Text;
using System.Text.Json;
using BlokeBot.Twitch;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Twitch.Tests;

public sealed class TransportClientTests
{
    [Test]
    public async Task ChatMessageSubscription_Creating_SendsTypedPayloadAndReturnsId()
    {
        var factory = new ScriptedHttpClientFactory();
        factory.Respond(
            async (request, cancellationToken) =>
            {
                AssertContext(request, HttpMethod.Post, "/helix/eventsub/subscriptions");
                using var payload = JsonDocument.Parse(
                    await request.Content!.ReadAsStringAsync(cancellationToken)
                );
                payload
                    .RootElement.GetProperty("type")
                    .GetString()
                    .ShouldBe("channel.chat.message");
                payload.RootElement.GetProperty("version").GetString().ShouldBe("1");
                var condition = payload.RootElement.GetProperty("condition");
                condition.GetProperty("broadcaster_user_id").GetString().ShouldBe("channel-id");
                condition.GetProperty("user_id").GetString().ShouldBe("bot-id");
                var transport = payload.RootElement.GetProperty("transport");
                transport.GetProperty("method").GetString().ShouldBe("websocket");
                transport.GetProperty("session_id").GetString().ShouldBe("session-id");
                return JsonResponse("""{"data":[{"id":"subscription-id"}]}""");
            }
        );
        var client = new EventSubClient(factory);

        var subscriptionId = await client.CreateChatMessageSubscriptionAsync(
            Context(),
            "channel-id",
            "bot-id",
            "session-id",
            CancellationToken.None
        );

        subscriptionId.ShouldBe("subscription-id");
    }

    [Test]
    public async Task ExistingSubscription_Deleting_SendsAuthenticatedDelete()
    {
        var factory = new ScriptedHttpClientFactory();
        factory.Respond(
            (request, _) =>
            {
                AssertContext(request, HttpMethod.Delete, "/helix/eventsub/subscriptions");
                request.RequestUri!.Query.ShouldBe("?id=subscription%2Fid");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            }
        );
        var client = new EventSubClient(factory);

        await client.DeleteSubscriptionAsync(Context(), "subscription/id", CancellationToken.None);

        factory.RequestCount.ShouldBe(1);
    }

    [Test]
    public async Task MissingSubscription_Deleting_TreatsNotFoundAsDeleted()
    {
        var factory = RespondingWith(HttpStatusCode.NotFound);
        var client = new EventSubClient(factory);

        await client.DeleteSubscriptionAsync(Context(), "missing", CancellationToken.None);

        factory.RequestCount.ShouldBe(1);
    }

    [Test]
    public async Task PublicChatMessage_Sending_MapsTypedProviderResult()
    {
        var factory = new ScriptedHttpClientFactory();
        factory.Respond(
            async (request, cancellationToken) =>
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
        var client = new ChatClient(factory);

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
            async (request, cancellationToken) =>
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
        var client = new WhisperClient(factory);

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
        var client = new WhisperClient(factory);

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
            (_, _) =>
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
        var client = new WhisperClient(factory);

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
        var client = new WhisperClient(factory);

        await Should.ThrowAsync<OperationCanceledException>(() =>
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
            async (request, cancellationToken) =>
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
        var client = new ChatAnnouncementClient(factory);

        var result = await client.SendAsync(
            Context(),
            "channel-id",
            "validated-bot-subject",
            "Native announcement",
            TwitchAnnouncementColor.Purple,
            CancellationToken.None
        );

        result.ShouldBeOfType<ChatAnnouncementSendResult.Sent>();
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
        var client = new ChatAnnouncementClient(RespondingWith(statusCode));

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
        var client = new ChatAnnouncementClient(factory);

        var result = await client.SendAsync(
            Context(),
            "channel-id",
            "validated-bot-subject",
            new string('x', 501),
            TwitchAnnouncementColor.Primary,
            CancellationToken.None
        );

        result.ShouldBeOfType<ChatAnnouncementSendResult.Invalid>();
        factory.RequestCount.ShouldBe(0);
    }

    [Test]
    public async Task NativeAnnouncement_UnsupportedColor_DoesNotSend()
    {
        var factory = new ScriptedHttpClientFactory();
        var client = new ChatAnnouncementClient(factory);

        var result = await client.SendAsync(
            Context(),
            "channel-id",
            "validated-bot-subject",
            "Native announcement",
            (TwitchAnnouncementColor)99,
            CancellationToken.None
        );

        result.ShouldBeOfType<ChatAnnouncementSendResult.Invalid>();
        factory.RequestCount.ShouldBe(0);
    }

    [Test]
    public async Task NativeAnnouncement_TransportFailure_RemainsAmbiguous()
    {
        var factory = new ScriptedHttpClientFactory();
        factory.Respond((_, _) => throw new HttpRequestException("connection lost"));
        var client = new ChatAnnouncementClient(factory);

        var result = await client.SendAsync(
            Context(),
            "channel-id",
            "validated-bot-subject",
            "Native announcement",
            TwitchAnnouncementColor.Primary,
            CancellationToken.None
        );

        result.ShouldBeOfType<ChatAnnouncementSendResult.Ambiguous>();
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
        var client = new ChatPinClient(factory);

        var result = await client.PinAsync(
            Context(),
            "channel-id",
            "bot-id",
            "exact-message-id",
            durationSeconds,
            CancellationToken.None
        );

        result.ShouldBeOfType<ChatPinMutationResult.Succeeded>();
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
        request.Headers.Authorization.Parameter.ShouldBe("access-token");
        request.Headers.GetValues("Client-Id").Single().ShouldBe("client-id");
    }

    private static HelixRequestContext Context()
    {
        return new("client-id", "access-token");
    }

    private static ScriptedHttpClientFactory RespondingWith(HttpStatusCode statusCode)
    {
        var factory = new ScriptedHttpClientFactory();
        factory.Respond((_, _) => Task.FromResult(new HttpResponseMessage(statusCode)));
        return factory;
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
        private readonly Queue<
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>
        > _responses = new();

        internal int RequestCount { get; private set; }

        internal void Respond(
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response
        )
        {
            _responses.Enqueue(response);
        }

        public HttpClient CreateClient(string name)
        {
            return new(new Handler(this), disposeHandler: false);
        }

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
