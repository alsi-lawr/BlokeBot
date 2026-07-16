using BlokeBot.Eventing;

namespace BlokeBot.Core.Features.SiteAccess;

public sealed class SiteAccessChangeNotifier(EventBus<AppEventKind> events)
    : EventNotifier<AppEventKind>(events, AppEventKind.SiteAccessChanged) { }
