using BlokeBot.AppEvents;

namespace BlokeBot.Features.Points;

public sealed class PointsChangeNotifier(AppEventBus events)
    : AppEventNotifier(events, AppEventKind.PointsChanged)
{
}
