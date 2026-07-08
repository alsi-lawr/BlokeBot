using BlokeBot.AppEvents;

namespace BlokeBot.Features.HostedChannels.Runtime;

public sealed class HostedChannelChangeNotifier(AppEventBus events)
    : AppEventNotifier(events, AppEventKind.HostedChannelsChanged)
{
}
