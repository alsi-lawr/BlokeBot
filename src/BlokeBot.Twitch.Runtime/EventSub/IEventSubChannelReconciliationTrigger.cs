namespace BlokeBot.Twitch.Runtime;

public interface IEventSubChannelReconciliationTrigger
{
    Task ReconcileAsync(CancellationToken cancellationToken);
}
