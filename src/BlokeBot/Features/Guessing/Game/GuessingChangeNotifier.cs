using BlokeBot.Eventing;

namespace BlokeBot.Features.Guessing.Game;

public sealed class GuessingChangeNotifier(EventBus<AppEventKind> events)
    : EventNotifier<AppEventKind>(events, AppEventKind.GuessingChanged)
{
}
