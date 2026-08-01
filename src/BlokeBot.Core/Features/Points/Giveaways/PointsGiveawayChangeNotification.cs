namespace BlokeBot.Core.Features.Points.Giveaways;

internal interface IPointsGiveawayChangeNotification
{
    ValueTask NotifyAsync(int hostId, CancellationToken cancellationToken);
}

public interface IPointsGiveawayChangeObserver
{
    ValueTask GiveawayChangedAsync(int hostId, CancellationToken cancellationToken);
}

public sealed class PointsGiveawayChangeNotifier(
    PointsChangeNotifier changes,
    IEnumerable<IPointsGiveawayChangeObserver> observers
)
{
    public PointsGiveawayChangeNotifier(PointsChangeNotifier changes)
        : this(changes, []) { }

    public async ValueTask NotifyChangedAsync(int hostId, CancellationToken cancellationToken)
    {
        await changes.NotifyChangedAsync(cancellationToken);
        foreach (var observer in observers)
        {
            await observer.GiveawayChangedAsync(hostId, cancellationToken);
        }
    }
}

internal sealed class PointsGiveawayChangeNotification(PointsGiveawayChangeNotifier changes)
    : IPointsGiveawayChangeNotification
{
    public async ValueTask NotifyAsync(int hostId, CancellationToken cancellationToken) =>
        await changes.NotifyChangedAsync(hostId, cancellationToken);
}

internal readonly record struct PointsGiveawayChangeNotificationCompleted;
