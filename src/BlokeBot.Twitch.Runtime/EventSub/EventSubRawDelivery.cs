using System.Text;
using System.Text.Json;

namespace BlokeBot.Twitch.Runtime;

internal sealed class EventSubRawDelivery(IEnumerable<IEventSubRawObserver>? observers = null)
{
    private readonly IEventSubRawObserver[] _observers = [.. observers ?? []];

    internal async Task DispatchAsync(
        EventSubEnvelope envelope,
        CancellationToken cancellationToken
    )
    {
        if (
            EventSubNotification.IsTypedSubscription(envelope.Metadata.SubscriptionType)
            || envelope.Payload.Event is not { ValueKind: JsonValueKind.Object } payload
            || !payload.TryGetProperty("broadcaster_user_login", out var broadcasterLogin)
            || broadcasterLogin.ValueKind is not JsonValueKind.String
            || string.IsNullOrWhiteSpace(broadcasterLogin.GetString())
            || envelope.Metadata.MessageTimestamp is not { } messageTimestamp
        )
        {
            return;
        }

        var notification = new EventSubRawNotification(
            envelope.Metadata.MessageId,
            messageTimestamp,
            envelope.Metadata.SubscriptionType,
            envelope.Metadata.SubscriptionVersion,
            broadcasterLogin.GetString()!,
            Encoding.UTF8.GetBytes(payload.GetRawText())
        );
        foreach (var observer in _observers)
        {
            await observer.RawEventReceivedAsync(notification, cancellationToken);
        }
    }
}
