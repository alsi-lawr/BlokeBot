using BlokeBot.Eventing;

namespace BlokeBot.Core.Features.Points;

public sealed class PointsChangeNotifier(EventBus<AppEventKind> events)
    : EventNotifier<AppEventKind>(events, AppEventKind.PointsChanged) { }
