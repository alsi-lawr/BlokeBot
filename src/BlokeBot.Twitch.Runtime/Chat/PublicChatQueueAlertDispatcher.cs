using BlokeBot.Eventing;

namespace BlokeBot.Twitch.Runtime;

internal sealed class PublicChatQueueAlertDispatcher(
    IEnumerable<IPublicChatQueueAlertObserver> observers,
    ObserverFanOut<
        PublicChatQueueAlertObserverBoundary,
        PublicChatQueueBacklog,
        PublicChatQueueAlertDeadLetter
    > fanOut
)
{
    private static readonly ObserverEventIdentity BacklogEvent =
        ObserverEventIdentity.Named("PublicChatQueueBacklog");
    private readonly IPublicChatQueueAlertObserver[] observers = [.. observers];

    public bool HasObservers => observers.Length > 0;

    public async Task NotifyAsync(
        IReadOnlyList<PublicChatQueueBacklog> alerts,
        CancellationToken cancellationToken
    )
    {
        foreach (var alert in alerts)
        {
            _ = await fanOut.DispatchAsync(
                observers,
                _ =>
                    new ObserverDispatch<
                        PublicChatQueueBacklog,
                        PublicChatQueueAlertDeadLetter
                    >
                    {
                        Event = alert,
                        EventIdentity = BacklogEvent,
                        DeadLetter = new PublicChatQueueAlertDeadLetter(
                            alert.Channel,
                            alert.PendingCount,
                            alert.OldestPendingAge,
                            alert.OldestPendingAt
                        ),
                    },
                observer => ObserverIdentity.For(observer.GetType()),
                static (observer, backlog, token) =>
                    observer.QueueBackedUpAsync(backlog, token),
                cancellationToken
            );
        }
    }
}
