using BlokeBot.Eventing;
using BlokeBot.Hosting;

namespace BlokeBot.Features.Alerts;

internal sealed class OutboundQueueAlertSubscriberDispatcher(
    IEnumerable<IOutboundQueueAlertSubscriber> subscribers,
    ObserverFanOut<
        OutboundQueueAlertSubscriberBoundary,
        OutboundQueueAlertNotification,
        OutboundQueueAlertSubscriberDeadLetter
    > fanOut
)
{
    private static readonly ObserverEventIdentity AlertCreatedEvent =
        ObserverEventIdentity.Named("OutboundQueueAlertCreated");
    private readonly IOutboundQueueAlertSubscriber[] subscribers = [.. subscribers];

    public async Task AlertCreatedAsync(
        OutboundQueueAlertNotification notification,
        CancellationToken cancellationToken
    )
    {
        _ = await fanOut.DispatchAsync(
            subscribers,
            _ =>
                new ObserverDispatch<
                    OutboundQueueAlertNotification,
                    OutboundQueueAlertSubscriberDeadLetter
                >
                {
                    Event = notification,
                    EventIdentity = AlertCreatedEvent,
                    DeadLetter = new OutboundQueueAlertSubscriberDeadLetter(
                        notification.AlertId,
                        notification.HostId,
                        notification.PendingCount,
                        notification.OldestPendingAge
                    ),
                },
            subscriber => ObserverIdentity.For(subscriber.GetType()),
            static (subscriber, alert, token) =>
                new ValueTask(subscriber.AlertCreatedAsync(alert, token)),
            cancellationToken
        );
    }
}
