using Microsoft.Extensions.Logging;

namespace BlokeBot.Twitch.Runtime;

internal sealed class TwitchOutboundQueueAlertDispatcher(
    IEnumerable<ITwitchOutboundQueueAlertObserver> observers,
    ILogger<TwitchOutboundQueueAlertDispatcher> log
)
{
    private readonly ITwitchOutboundQueueAlertObserver[] observers = observers.ToArray();

    public bool HasObservers => observers.Length > 0;

    public async Task NotifyAsync(IReadOnlyList<TwitchOutboundQueueBacklog> alerts)
    {
        foreach (var alert in alerts)
        {
            foreach (var observer in observers)
            {
                try
                {
                    await observer.QueueBackedUpAsync(alert, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    log.LogWarning(
                        ex,
                        "Twitch outbound queue alert observer failed for #{Channel}.",
                        alert.Channel
                    );
                }
            }
        }
    }
}
