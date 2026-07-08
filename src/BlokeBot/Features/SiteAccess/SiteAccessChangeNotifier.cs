using BlokeBot.Eventing;

namespace BlokeBot.Features.SiteAccess;

public sealed class SiteAccessChangeNotifier(EventBus<AppEventKind> events)
    : EventNotifier<AppEventKind>(events, AppEventKind.SiteAccessChanged)
{
}
