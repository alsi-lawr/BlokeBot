using BlokeBot.AppEvents;

namespace BlokeBot.Features.Guessing.Game;

public sealed class GuessingChangeNotifier(AppEventBus events)
    : AppEventNotifier(events, AppEventKind.GuessingChanged)
{
}
