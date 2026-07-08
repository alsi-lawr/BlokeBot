using BlokeBot.AppEvents;

namespace BlokeBot.Features.Guessing.Game;

public sealed class GuessingChangeNotifier(AppEventBus events)
{
    public event Func<Task>? Changed;

    public async Task NotifyChangedAsync()
    {
        await events.PublishAsync(AppEventKind.GuessingChanged);

        var handlers = Changed;
        if (handlers is null)
            return;

        foreach (Func<Task> handler in handlers.GetInvocationList())
            await handler();
    }
}
