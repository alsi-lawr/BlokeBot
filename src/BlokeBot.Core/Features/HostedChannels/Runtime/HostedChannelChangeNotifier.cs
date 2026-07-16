using BlokeBot.Eventing;

namespace BlokeBot.Core.Features.HostedChannels.Runtime;

public sealed class HostedChannelChangeNotifier(EventBus<AppEventKind> events)
    : EventNotifier<AppEventKind>(events, AppEventKind.HostedChannelsChanged) { }
