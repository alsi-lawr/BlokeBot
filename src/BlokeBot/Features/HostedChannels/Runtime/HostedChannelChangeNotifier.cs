using BlokeBot.AppEvents;

namespace BlokeBot.Features.HostedChannels.Runtime;

public sealed class HostedChannelChangeNotifier(AppEventBus events)
{
    public event Func<Task>? Changed;

    public async Task NotifyChangedAsync()
    {
        await events.PublishAsync(AppEventKind.HostedChannelsChanged);

        if (Changed is not { } changed)
            return;

        foreach (var handler in changed.GetInvocationList())
            await ((Func<Task>)handler)();
    }
}
