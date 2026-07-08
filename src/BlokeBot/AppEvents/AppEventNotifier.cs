namespace BlokeBot.AppEvents;

public abstract class AppEventNotifier(AppEventBus events, AppEventKind kind)
{
    public Task NotifyChangedAsync() => events.PublishAsync(kind);
}
