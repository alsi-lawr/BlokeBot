using BlokeBot.Eventing;
using BlokeBot.Features.Alerts;

namespace BlokeBot.Hosting;

internal static class BlokeBotObserverBoundaries
{
    internal static ObserverBoundary OutboundQueueAlertSubscribers { get; } =
        ObserverBoundary.Named("BlokeBot.OutboundQueueAlertSubscribers");
}

internal sealed class OutboundQueueAlertSubscriberBoundary;

internal sealed record OutboundQueueAlertSubscriberDeadLetter(
    int AlertId,
    int HostId,
    int PendingCount,
    TimeSpan OldestPendingAge
) : IObserverDeadLetterPayload;
