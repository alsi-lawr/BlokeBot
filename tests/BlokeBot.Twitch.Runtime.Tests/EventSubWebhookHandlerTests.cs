using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace BlokeBot.Twitch.Runtime.Tests;

public sealed class EventSubWebhookHandlerTests
{
    private const string _secret = "webhook-test-secret";
    private static readonly DateTimeOffset _now = new(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task ValidSignedVerification_ReturnsExactPlainChallengeValue()
    {
        var verification = new RecordingVerification();
        var handler = CreateHandler(verification: verification);
        var body = Body("webhook_callback_verification", challenge: "exact challenge");

        var result = await HandleAsync(
            handler,
            "verification-1",
            "webhook_callback_verification",
            body
        );

        result.ShouldBe(new EventSubWebhookResult(200, "exact challenge"));
        verification.ConfirmedIds.ShouldBe(["subscription-1"]);
    }

    [Test]
    public async Task ValidSignedNotification_IsAcknowledgedAndDispatchedWithOfficialMetadata()
    {
        var delivery = new RecordingDelivery();
        var handler = CreateHandler(delivery);
        await handler.StartAsync(CancellationToken.None);
        var body = Body("notification");

        var result = await HandleAsync(handler, "notification-1", "notification", body);
        await delivery.Next.WaitAsync(TimeSpan.FromSeconds(2));
        await handler.StopAsync(CancellationToken.None);

        result.StatusCode.ShouldBe(202);
        var dispatched = delivery.Deliveries.ShouldHaveSingleItem();
        dispatched.Envelope.Metadata.MessageId.ShouldBe("notification-1");
        dispatched.Envelope.Metadata.SubscriptionType.ShouldBe("channel.chat.message");
        dispatched.RawJson.ShouldBe(Encoding.UTF8.GetString(body));
    }

    [Test]
    public async Task ValidSignedRevocation_RepairsTheRevokedSubscription()
    {
        var reconciliation = new RecordingReconciliation();
        var handler = CreateHandler(reconciliation: reconciliation);
        await handler.StartAsync(CancellationToken.None);

        var result = await HandleAsync(handler, "revocation-1", "revocation", Body("revocation"));
        await reconciliation.Next.WaitAsync(TimeSpan.FromSeconds(2));
        await handler.StopAsync(CancellationToken.None);

        result.StatusCode.ShouldBe(202);
        reconciliation.RevokedIds.ShouldBe(["subscription-1"]);
    }

    [Test]
    public async Task DuplicateMessageId_IsAcknowledgedWithoutSecondDispatch()
    {
        var delivery = new RecordingDelivery();
        var handler = CreateHandler(delivery);
        await handler.StartAsync(CancellationToken.None);
        var body = Body("notification");

        var first = await HandleAsync(handler, "duplicate-1", "notification", body);
        var duplicate = await HandleAsync(handler, "duplicate-1", "notification", body);
        await delivery.Next.WaitAsync(TimeSpan.FromSeconds(2));
        await handler.StopAsync(CancellationToken.None);

        first.StatusCode.ShouldBe(202);
        duplicate.StatusCode.ShouldBe(200);
        delivery.Deliveries.Count.ShouldBe(1);
    }

    [Test]
    public async Task ReplayWindow_AcceptsExactBoundaryAndRejectsPastOrFutureOutsideBoundary()
    {
        var handler = CreateHandler();
        var body = Body("notification");

        (
            await HandleAsync(handler, "boundary", "notification", body, _now.AddMinutes(-10))
        ).StatusCode.ShouldBe(202);
        (
            await HandleAsync(
                handler,
                "stale",
                "notification",
                body,
                _now.AddMinutes(-10).AddTicks(-1)
            )
        ).StatusCode.ShouldBe(403);
        (
            await HandleAsync(
                handler,
                "future",
                "notification",
                body,
                _now.AddMinutes(10).AddTicks(1)
            )
        ).StatusCode.ShouldBe(403);
    }

    [Test]
    public async Task MissingMalformedUnknownAndOversizedDeliveries_HaveExplicitSafeOutcomes()
    {
        var handler = CreateHandler();
        var validBody = Body("notification");
        var timestamp = _now.ToString("O");

        (
            await handler.HandleAsync(
                null,
                "notification",
                timestamp,
                "sha256=00",
                "channel.chat.message",
                "1",
                validBody,
                CancellationToken.None
            )
        ).StatusCode.ShouldBe(403);
        (
            await handler.HandleAsync(
                "malformed-signature",
                "notification",
                timestamp,
                "sha256=not-hex",
                "channel.chat.message",
                "1",
                validBody,
                CancellationToken.None
            )
        ).StatusCode.ShouldBe(403);

        var malformed = Encoding.UTF8.GetBytes("{");
        (
            await HandleAsync(handler, "malformed-json", "notification", malformed)
        ).StatusCode.ShouldBe(400);
        (await HandleAsync(handler, "unknown", "unknown-message", validBody)).StatusCode.ShouldBe(
            400
        );

        var oversized = new byte[(512 * 1024) + 1];
        (await HandleAsync(handler, "oversized", "notification", oversized)).StatusCode.ShouldBe(
            413
        );
    }

    [Test]
    public async Task FullQueue_ReturnsRetryableFailureWithoutPoisoningDedupe()
    {
        var delivery = new RecordingDelivery();
        var handler = CreateHandler(delivery);
        var body = Body("notification");
        for (var index = 0; index < 256; index++)
        {
            (
                await HandleAsync(handler, $"queued-{index}", "notification", body)
            ).StatusCode.ShouldBe(202);
        }

        (await HandleAsync(handler, "retry-me", "notification", body)).StatusCode.ShouldBe(503);

        await handler.StartAsync(CancellationToken.None);
        await delivery.WaitForCountAsync(256);
        (await HandleAsync(handler, "retry-me", "notification", body)).StatusCode.ShouldBe(202);
        await delivery.WaitForCountAsync(257);
        await handler.StopAsync(CancellationToken.None);
    }

    [Test]
    public async Task StoppedQueue_ReturnsRetryableFailure()
    {
        var handler = CreateHandler();
        await handler.StartAsync(CancellationToken.None);
        await handler.StopAsync(CancellationToken.None);

        var result = await HandleAsync(handler, "after-stop", "notification", Body("notification"));

        result.StatusCode.ShouldBe(503);
    }

    [Test]
    public async Task Stop_DrainsDeliveryAlreadyAcknowledgedAccepted()
    {
        var delivery = new BlockingDelivery();
        var handler = CreateHandler(delivery);
        await handler.StartAsync(CancellationToken.None);
        (
            await HandleAsync(handler, "drain-me", "notification", Body("notification"))
        ).StatusCode.ShouldBe(202);
        await delivery.Started.WaitAsync(TimeSpan.FromSeconds(2));

        var stopping = handler.StopAsync(CancellationToken.None);
        await Task.Delay(20);
        stopping.IsCompleted.ShouldBeFalse();
        delivery.Release();
        await stopping;

        delivery.Completed.ShouldBeTrue();
    }

    [Test]
    public async Task DeliveryFailure_LogsOnlyBoundedSafeClassification()
    {
        const string RawMarker = "raw-callback-marker";
        const string SignatureMarker = "signature-marker";
        const string TokenMarker = "authorization-token-marker";
        var callback = "https://bot.blokebot.com/eventsub/twitch";
        var logger = new RecordingLogger();
        var delivery = new ThrowingDelivery(
            $"{RawMarker} {_secret} {callback} {SignatureMarker} {TokenMarker}"
        );
        var handler = CreateHandler(delivery, logger: logger);
        await handler.StartAsync(CancellationToken.None);

        _ = await HandleAsync(
            handler,
            "safe-log",
            "notification",
            Body("notification", eventText: RawMarker)
        );
        await delivery.Called.WaitAsync(TimeSpan.FromSeconds(2));
        await logger.Next.WaitAsync(TimeSpan.FromSeconds(2));
        await handler.StopAsync(CancellationToken.None);

        var log = logger.Messages.ShouldHaveSingleItem();
        log.ShouldContain(nameof(InvalidOperationException));
        log.ShouldNotContain(RawMarker);
        log.ShouldNotContain(_secret);
        log.ShouldNotContain(callback);
        log.ShouldNotContain(SignatureMarker);
        log.ShouldNotContain(TokenMarker);
    }

    private static EventSubWebhookHandler CreateHandler(
        IEventSubDeliveryHandler? delivery = null,
        IEventSubChannelReconciliationTrigger? reconciliation = null,
        IEventSubSubscriptionVerification? verification = null,
        ILogger<EventSubWebhookHandler>? logger = null
    ) =>
        new(
            new EventSubWebhookOptions
            {
                CallbackUri = new Uri("https://bot.blokebot.com/eventsub/twitch"),
                Secret = _secret,
            },
            delivery ?? new RecordingDelivery(),
            reconciliation ?? new RecordingReconciliation(),
            verification ?? new RecordingVerification(),
            new FixedTimeProvider(_now),
            logger ?? new RecordingLogger()
        );

    private static async ValueTask<EventSubWebhookResult> HandleAsync(
        EventSubWebhookHandler handler,
        string messageId,
        string messageType,
        byte[] body,
        DateTimeOffset? timestamp = null
    )
    {
        var value = (timestamp ?? _now).ToString("O");
        return await handler.HandleAsync(
            messageId,
            messageType,
            value,
            Sign(messageId, value, body),
            "channel.chat.message",
            "1",
            body,
            CancellationToken.None
        );
    }

    private static byte[] Body(
        string messageType,
        string? challenge = null,
        string eventText = "hello"
    ) =>
        JsonSerializer.SerializeToUtf8Bytes(
            new
            {
                challenge,
                subscription = new
                {
                    id = "subscription-1",
                    status = "enabled",
                    type = "channel.chat.message",
                    version = "1",
                    condition = new { broadcaster_user_id = "channel-id", user_id = "bot-id" },
                    transport = new
                    {
                        method = "webhook",
                        callback = "https://bot.blokebot.com/eventsub/twitch",
                    },
                    created_at = "2026-08-03T11:00:00Z",
                    cost = 0,
                },
                @event = messageType == "notification"
                    ? new
                    {
                        broadcaster_user_id = "channel-id",
                        broadcaster_user_login = "channel",
                        chatter_user_id = "viewer-id",
                        chatter_user_login = "viewer",
                        message_id = "chat-id",
                        message = new { text = eventText },
                        badges = Array.Empty<object>(),
                    }
                    : null,
            }
        );

    private static string Sign(string messageId, string timestamp, byte[] body)
    {
        var prefix = Encoding.UTF8.GetBytes(messageId + timestamp);
        var data = new byte[prefix.Length + body.Length];
        prefix.CopyTo(data, 0);
        body.CopyTo(data, prefix.Length);
        return "sha256="
            + Convert
                .ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(_secret), data))
                .ToLowerInvariant();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecordingDelivery : IEventSubDeliveryHandler
    {
        private readonly TaskCompletionSource _next = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        internal List<(EventSubEnvelope Envelope, string RawJson)> Deliveries { get; } = [];

        internal Task Next => _next.Task;

        public Task DispatchNotificationAsync(
            EventSubEnvelope envelope,
            string rawJson,
            CancellationToken cancellationToken
        )
        {
            Deliveries.Add((envelope, rawJson));
            _ = _next.TrySetResult();
            return Task.CompletedTask;
        }

        internal async Task WaitForCountAsync(int count)
        {
            var timeout = DateTime.UtcNow.AddSeconds(2);
            while (Deliveries.Count < count && DateTime.UtcNow < timeout)
            {
                await Task.Delay(1);
            }

            Deliveries.Count.ShouldBe(count);
        }
    }

    private sealed class BlockingDelivery : IEventSubDeliveryHandler
    {
        private readonly TaskCompletionSource _release = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly TaskCompletionSource _started = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        internal Task Started => _started.Task;

        internal bool Completed { get; private set; }

        public async Task DispatchNotificationAsync(
            EventSubEnvelope envelope,
            string rawJson,
            CancellationToken cancellationToken
        )
        {
            _ = _started.TrySetResult();
            await _release.Task;
            Completed = true;
        }

        internal void Release() => _release.TrySetResult();
    }

    private sealed class ThrowingDelivery(string message) : IEventSubDeliveryHandler
    {
        private readonly TaskCompletionSource _called = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        internal Task Called => _called.Task;

        public Task DispatchNotificationAsync(
            EventSubEnvelope envelope,
            string rawJson,
            CancellationToken cancellationToken
        )
        {
            _ = _called.TrySetResult();
            throw new InvalidOperationException(message);
        }
    }

    private sealed class RecordingReconciliation : IEventSubChannelReconciliationTrigger
    {
        private readonly TaskCompletionSource _next = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        internal Task Next => _next.Task;

        internal List<string> RevokedIds { get; } = [];

        public Task ReconcileAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ReconcileRevocationAsync(
            string subscriptionId,
            CancellationToken cancellationToken
        )
        {
            RevokedIds.Add(subscriptionId);
            _ = _next.TrySetResult();
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingVerification : IEventSubSubscriptionVerification
    {
        internal List<string> ConfirmedIds { get; } = [];

        public Task WaitAsync(string subscriptionId, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public void Confirm(string subscriptionId) => ConfirmedIds.Add(subscriptionId);
    }

    private sealed class RecordingLogger : ILogger<EventSubWebhookHandler>
    {
        private readonly TaskCompletionSource _next = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        internal List<string> Messages { get; } = [];

        internal Task Next => _next.Task;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            Messages.Add(formatter(state, exception));
            _ = _next.TrySetResult();
        }
    }
}
