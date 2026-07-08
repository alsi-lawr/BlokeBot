using BlokeBot.AppEvents;

namespace BlokeBot.Features.SiteAccess;

public sealed class SiteAccessChangeNotifier(AppEventBus events)
    : AppEventNotifier(events, AppEventKind.SiteAccessChanged)
{
}
