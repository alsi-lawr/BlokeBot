namespace BlokeBot.Twitch.Runtime;

public interface IShoutoutEventObserver
{
    Task ShoutoutReceivedAsync(EventSubShoutoutEvent shoutout, CancellationToken cancellationToken);
}

public sealed record EventSubShoutoutEvent(
    string BroadcasterUserId,
    string BroadcasterUserLogin,
    string FromBroadcasterUserId,
    string FromBroadcasterUserLogin,
    string ToBroadcasterUserId,
    string ToBroadcasterUserLogin,
    int ViewerCount,
    DateTimeOffset StartedAt,
    DateTimeOffset? CooldownEndsAt,
    DateTimeOffset? TargetCooldownEndsAt,
    EventSubShoutoutDirection Direction,
    string MessageId
);

public enum EventSubShoutoutDirection
{
    Sent,
    Received,
}
