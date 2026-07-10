namespace BlokeBot.Features.Alerts;

public sealed record OutboundQueueAlertNotification(
    int AlertId,
    int HostId,
    string HostLogin,
    string? HostTwitchUserId,
    int PendingCount,
    TimeSpan OldestPendingAge
);

public interface IOutboundQueueAlertSubscriber
{
    Task AlertCreatedAsync(OutboundQueueAlertNotification notification, CancellationToken ct);
}
