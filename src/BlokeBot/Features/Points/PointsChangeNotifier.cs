using BlokeBot.Eventing;

namespace BlokeBot.Features.Points;

public sealed class PointsChangeNotifier(EventBus<AppEventKind> events)
    : EventNotifier<AppEventKind>(events, AppEventKind.PointsChanged)
{
}
