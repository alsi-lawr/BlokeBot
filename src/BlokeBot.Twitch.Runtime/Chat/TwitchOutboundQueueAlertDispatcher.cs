using BlokeBot.Eventing;

namespace BlokeBot.Twitch.Runtime;

internal sealed class TwitchOutboundQueueAlertDispatcher(
    IEnumerable<ITwitchOutboundQueueAlertObserver> observers,
    ObserverFanOut<
        TwitchOutboundQueueAlertObserverBoundary,
        TwitchOutboundQueueBacklog,
        TwitchOutboundQueueAlertDeadLetter
    > fanOut
)
{
    private static readonly ObserverEventIdentity BacklogEvent =
        ObserverEventIdentity.Named("TwitchOutboundQueueBacklog");
    private readonly ITwitchOutboundQueueAlertObserver[] observers = [.. observers];

    public bool HasObservers => observers.Length > 0;

    public async Task NotifyAsync(
        IReadOnlyList<TwitchOutboundQueueBacklog> alerts,
        CancellationToken cancellationToken
    )
    {
        foreach (var alert in alerts)
        {
            _ = await fanOut.DispatchAsync(
                observers,
                _ =>
                    new ObserverDispatch<
                        TwitchOutboundQueueBacklog,
                        TwitchOutboundQueueAlertDeadLetter
                    >
                    {
                        Event = alert,
                        EventIdentity = BacklogEvent,
                        DeadLetter = new TwitchOutboundQueueAlertDeadLetter(
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
