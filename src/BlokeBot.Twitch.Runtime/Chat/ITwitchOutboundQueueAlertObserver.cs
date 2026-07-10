namespace BlokeBot.Twitch.Runtime;

public sealed record TwitchOutboundQueueBacklog(
    string Channel,
    int PendingCount,
    TimeSpan OldestPendingAge,
    DateTimeOffset OldestPendingAt
);

public interface ITwitchOutboundQueueAlertObserver
{
    ValueTask QueueBackedUpAsync(
        TwitchOutboundQueueBacklog backlog,
        CancellationToken cancellationToken
    );
}
