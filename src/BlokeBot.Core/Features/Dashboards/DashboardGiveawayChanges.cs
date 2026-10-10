using BlokeBot.Core.Features.Points.Giveaways;
using BlokeBot.Eventing;

namespace BlokeBot.Core.Features.Dashboards;

internal readonly record struct DashboardGiveawayChange(DashboardHostId Host);

internal sealed class DashboardGiveawayChanges(EventBus<DashboardGiveawayChange> events)
    : IPointsGiveawayChangeObserver
{
    internal IDisposable Subscribe(
        DashboardHostId host,
        ObserverIdentity observer,
        Func<EventNotification<DashboardGiveawayChange>, CancellationToken, ValueTask> handler
    ) => events.Subscribe(new DashboardGiveawayChange(host), observer, handler);

    public async ValueTask GiveawayChangedAsync(int hostId, CancellationToken cancellationToken) =>
        _ = await events.PublishAsync(new(new(hostId)), cancellationToken);
}
