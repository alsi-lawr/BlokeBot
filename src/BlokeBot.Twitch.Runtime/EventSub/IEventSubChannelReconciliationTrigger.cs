namespace BlokeBot.Twitch.Runtime;

public interface IEventSubChannelReconciliationTrigger
{
    Task ReconcileAsync(CancellationToken cancellationToken);

    Task ReconcileRevocationAsync(string subscriptionId, CancellationToken cancellationToken);
}
