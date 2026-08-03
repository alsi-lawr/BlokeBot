using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BlokeBot.Twitch.Runtime;

public interface IEventSubWebhookIngress
{
    ValueTask<EventSubWebhookResult> HandleAsync(
        string? messageId,
        string? messageType,
        string? timestamp,
        string? signature,
        string? subscriptionType,
        string? subscriptionVersion,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken
    );
}

public sealed record EventSubWebhookResult(int StatusCode, string? Challenge = null)
{
    public bool IsAccepted => StatusCode is >= 200 and < 300;
}

internal sealed class EventSubWebhookHandler(
    EventSubWebhookOptions options,
    IEventSubDeliveryHandler delivery,
    IEventSubChannelReconciliationTrigger reconciliation,
    IEventSubSubscriptionVerification verification,
    TimeProvider timeProvider,
    ILogger<EventSubWebhookHandler> log
) : BackgroundService, IEventSubWebhookIngress
{
    private const int _maxBodyBytes = 512 * 1024;
    private static readonly TimeSpan _replayWindow = TimeSpan.FromMinutes(10);
    private readonly Channel<QueuedDelivery> _queue = Channel.CreateBounded<QueuedDelivery>(
        new BoundedChannelOptions(256)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        }
    );
    private readonly HashSet<string> _messageIds = new(StringComparer.Ordinal);
    private readonly Queue<string> _messageIdOrder = new();
    private readonly object _gate = new();
    private int _accepting = 1;

    public ValueTask<EventSubWebhookResult> HandleAsync(
        string? messageId,
        string? messageType,
        string? timestamp,
        string? signature,
        string? subscriptionType,
        string? subscriptionVersion,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken
    )
    {
        if (body.Length > _maxBodyBytes)
        {
            return ValueTask.FromResult(new EventSubWebhookResult(413));
        }

        if (!IsAuthentic(messageId, timestamp, signature, body.Span))
        {
            return ValueTask.FromResult(new EventSubWebhookResult(403));
        }

        EventSubEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<EventSubEnvelope>(body.Span);
        }
        catch (JsonException)
        {
            return ValueTask.FromResult(new EventSubWebhookResult(400));
        }

        if (
            envelope is null
            || string.IsNullOrWhiteSpace(messageId)
            || string.IsNullOrWhiteSpace(messageType)
            || string.IsNullOrWhiteSpace(subscriptionType)
            || string.IsNullOrWhiteSpace(subscriptionVersion)
            || envelope.Subscription is not { ValueKind: JsonValueKind.Object }
        )
        {
            return ValueTask.FromResult(new EventSubWebhookResult(400));
        }

        if (
            !messageType.Equals("webhook_callback_verification", StringComparison.Ordinal)
            && !messageType.Equals("notification", StringComparison.Ordinal)
            && !messageType.Equals("revocation", StringComparison.Ordinal)
        )
        {
            return ValueTask.FromResult(new EventSubWebhookResult(400));
        }

        envelope.Metadata = new EventSubMetadata
        {
            MessageId = messageId,
            MessageType = messageType,
            MessageTimestamp = DateTimeOffset.TryParse(timestamp, out var parsed) ? parsed : null,
            SubscriptionType = subscriptionType,
            SubscriptionVersion = subscriptionVersion,
        };
        var subscriptionId =
            envelope.Subscription.Value.TryGetProperty("id", out var id)
            && id.ValueKind is JsonValueKind.String
                ? id.GetString() ?? string.Empty
                : string.Empty;

        if (messageType.Equals("webhook_callback_verification", StringComparison.Ordinal))
        {
            var challenge = envelope.Challenge;
            if (string.IsNullOrEmpty(challenge) || string.IsNullOrWhiteSpace(subscriptionId))
            {
                return ValueTask.FromResult(new EventSubWebhookResult(400));
            }

            verification.Confirm(subscriptionId);
            return ValueTask.FromResult(new EventSubWebhookResult(200, challenge));
        }

        if (
            messageType.Equals("notification", StringComparison.Ordinal)
            && envelope.Event is not { ValueKind: JsonValueKind.Object }
        )
        {
            return ValueTask.FromResult(new EventSubWebhookResult(400));
        }
        if (
            messageType.Equals("revocation", StringComparison.Ordinal)
            && string.IsNullOrWhiteSpace(subscriptionId)
        )
        {
            return ValueTask.FromResult(new EventSubWebhookResult(400));
        }

        lock (_gate)
        {
            if (Volatile.Read(ref _accepting) is 0)
            {
                return ValueTask.FromResult(new EventSubWebhookResult(503));
            }

            if (_messageIds.Contains(messageId))
            {
                return ValueTask.FromResult(new EventSubWebhookResult(200));
            }

            if (
                !_queue.Writer.TryWrite(
                    new QueuedDelivery(envelope, Encoding.UTF8.GetString(body.Span), subscriptionId)
                )
            )
            {
                return ValueTask.FromResult(new EventSubWebhookResult(503));
            }

            _ = _messageIds.Add(messageId);
            _messageIdOrder.Enqueue(messageId);
            if (_messageIdOrder.Count > 512)
            {
                _ = _messageIds.Remove(_messageIdOrder.Dequeue());
            }
        }

        return ValueTask.FromResult(new EventSubWebhookResult(202));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var queued in _queue.Reader.ReadAllAsync())
        {
            try
            {
                if (
                    queued.Envelope.Metadata.MessageType.Equals(
                        "revocation",
                        StringComparison.Ordinal
                    )
                )
                {
                    log.LogWarning("Twitch revoked an EventSub subscription.");
                    await reconciliation.ReconcileRevocationAsync(
                        queued.SubscriptionId,
                        CancellationToken.None
                    );
                }
                else if (
                    queued.Envelope.Metadata.MessageType.Equals(
                        "notification",
                        StringComparison.Ordinal
                    )
                )
                {
                    await delivery.DispatchNotificationAsync(
                        queued.Envelope,
                        queued.RawJson,
                        CancellationToken.None
                    );
                }
            }
            catch (Exception exception)
            {
                log.LogError(
                    "Twitch EventSub webhook delivery failed with {FailureType}.",
                    exception.GetType().Name
                );
            }
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _ = Interlocked.Exchange(ref _accepting, 0);
        _ = _queue.Writer.TryComplete();
        return base.StopAsync(cancellationToken);
    }

    private bool IsAuthentic(
        string? messageId,
        string? timestamp,
        string? signature,
        ReadOnlySpan<byte> body
    )
    {
        if (
            string.IsNullOrWhiteSpace(messageId)
            || string.IsNullOrWhiteSpace(timestamp)
            || string.IsNullOrWhiteSpace(signature)
            || !DateTimeOffset.TryParse(timestamp, out var parsed)
            || timeProvider.GetUtcNow() - parsed > _replayWindow
            || parsed - timeProvider.GetUtcNow() > _replayWindow
            || !signature.StartsWith("sha256=", StringComparison.Ordinal)
        )
        {
            return false;
        }

        var prefix = Encoding.UTF8.GetBytes(messageId + timestamp);
        var signed = new byte[prefix.Length + body.Length];
        prefix.CopyTo(signed, 0);
        body.CopyTo(signed.AsSpan(prefix.Length));
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(options.Secret), signed);
        try
        {
            var supplied = Convert.FromHexString(signature[7..]);
            return CryptographicOperations.FixedTimeEquals(expected, supplied);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private sealed record QueuedDelivery(
        EventSubEnvelope Envelope,
        string RawJson,
        string SubscriptionId
    );
}

internal sealed class EventSubSubscriptionVerification : IEventSubSubscriptionVerification
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _confirmations = new(
        StringComparer.Ordinal
    );

    public async Task WaitAsync(string subscriptionId, CancellationToken cancellationToken)
    {
        var confirmation = _confirmations.GetOrAdd(
            subscriptionId,
            static _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
        );
        try
        {
            await confirmation.Task.WaitAsync(cancellationToken);
        }
        finally
        {
            _ = _confirmations.TryRemove(
                new KeyValuePair<string, TaskCompletionSource>(subscriptionId, confirmation)
            );
        }
    }

    public void Confirm(string subscriptionId) =>
        _confirmations
            .GetOrAdd(
                subscriptionId,
                static _ => new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously
                )
            )
            .TrySetResult();
}
