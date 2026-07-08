using BlokeBot.AppEvents;

namespace BlokeBot.Features.Points;

public sealed class PointsChangeNotifier(AppEventBus events)
{
    public event Func<Task>? Changed;

    public async Task NotifyChangedAsync()
    {
        await events.PublishAsync(AppEventKind.PointsChanged);

        var handlers = Changed;
        if (handlers is null)
            return;

        foreach (Func<Task> handler in handlers.GetInvocationList())
            await handler();
    }
}
