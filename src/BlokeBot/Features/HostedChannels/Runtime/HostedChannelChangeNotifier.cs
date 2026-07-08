using BlokeBot.Eventing;

namespace BlokeBot.Features.HostedChannels.Runtime;

public sealed class HostedChannelChangeNotifier(EventBus<AppEventKind> events)
    : EventNotifier<AppEventKind>(events, AppEventKind.HostedChannelsChanged)
{
}
