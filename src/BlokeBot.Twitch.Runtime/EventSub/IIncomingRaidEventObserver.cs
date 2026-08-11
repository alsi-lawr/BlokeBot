namespace BlokeBot.Twitch.Runtime;

public interface IIncomingRaidEventObserver
{
    Task IncomingRaidReceivedAsync(
        EventSubIncomingRaidEvent incomingRaid,
        CancellationToken cancellationToken
    );
}

public sealed record EventSubIncomingRaidEvent(
    string MessageId,
    DateTimeOffset MessageTimestamp,
    string FromBroadcasterUserId,
    string FromBroadcasterUserLogin,
    string FromBroadcasterUserName,
    string ToBroadcasterUserId,
    string ToBroadcasterUserLogin,
    string ToBroadcasterUserName,
    int ViewerCount,
    EventSubRaidSubscriptionDirection SubscriptionDirection =
        EventSubRaidSubscriptionDirection.Incoming
);

public enum EventSubRaidSubscriptionDirection
{
    Incoming,
    Outgoing,
}
