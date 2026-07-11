namespace BlokeBot.Features.Points.Giveaways;

internal interface IPointsGiveawayChangeNotification
{
    ValueTask NotifyAsync(CancellationToken cancellationToken);
}

internal sealed class PointsGiveawayChangeNotification(PointsChangeNotifier changes)
    : IPointsGiveawayChangeNotification
{
    public async ValueTask NotifyAsync(CancellationToken cancellationToken) =>
        await changes.NotifyChangedAsync(cancellationToken);
}

internal readonly record struct PointsGiveawayChangeNotificationCompleted;
