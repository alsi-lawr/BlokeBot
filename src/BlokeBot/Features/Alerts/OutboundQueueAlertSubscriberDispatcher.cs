using Microsoft.Extensions.Logging;

namespace BlokeBot.Features.Alerts;

internal sealed class OutboundQueueAlertSubscriberDispatcher(
    IEnumerable<IOutboundQueueAlertSubscriber> subscribers,
    ILogger<OutboundQueueAlertSubscriberDispatcher> log
)
{
    public async Task AlertCreatedAsync(
        OutboundQueueAlertNotification notification,
        CancellationToken ct
    )
    {
        foreach (var subscriber in subscribers)
        {
            try
            {
                await subscriber.AlertCreatedAsync(notification, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                log.LogWarning(
                    ex,
                    "Outbound queue alert subscriber {SubscriberType} failed for alert {AlertId}.",
                    subscriber.GetType().Name,
                    notification.AlertId
                );
            }
        }
    }
}
