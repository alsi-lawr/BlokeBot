namespace BlokeBot.Twitch.Runtime;

public sealed record EventSubRawNotification(
    string MessageId,
    DateTimeOffset MessageTimestamp,
    string SubscriptionType,
    string SubscriptionVersion,
    string BroadcasterUserLogin,
    ReadOnlyMemory<byte> EventJson
);

public interface IEventSubRawObserver
{
    Task RawEventReceivedAsync(
        EventSubRawNotification notification,
        CancellationToken cancellationToken
    );
}
