namespace BlokeBot.Twitch.Runtime;

public sealed record PublicChatQueueBacklog(
    string Channel,
    int PendingCount,
    TimeSpan OldestPendingAge,
    DateTimeOffset OldestPendingAt
);

public interface IPublicChatQueueAlertObserver
{
    ValueTask QueueBackedUpAsync(
        PublicChatQueueBacklog backlog,
        CancellationToken cancellationToken
    );
}
