namespace BlokeBot.Twitch.Runtime;

internal sealed class NoOpEventSubChannelReconciliationTrigger
    : IEventSubChannelReconciliationTrigger
{
    public Task ReconcileAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
