using BlokeBot.AppEvents;

namespace BlokeBot.Features.SiteAccess;

public sealed class SiteAccessChangeNotifier(AppEventBus events)
{
    public event Func<Task>? Changed;

    public async Task NotifyChangedAsync()
    {
        await events.PublishAsync(AppEventKind.SiteAccessChanged);

        if (Changed is { } changed)
            await changed.Invoke();
    }
}
