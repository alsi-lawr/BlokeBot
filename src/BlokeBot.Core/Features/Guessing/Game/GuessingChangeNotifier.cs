using BlokeBot.Eventing;

namespace BlokeBot.Core.Features.Guessing.Game;

public interface IGuessingChangeObserver
{
    ValueTask GuessingChangedAsync(int hostId, CancellationToken cancellationToken);
}

public sealed class GuessingChangeNotifier(
    EventBus<AppEventKind> events,
    IEnumerable<IGuessingChangeObserver> observers
)
{
    public GuessingChangeNotifier(EventBus<AppEventKind> events)
        : this(events, []) { }

    public async ValueTask NotifyChangedAsync(int hostId, CancellationToken cancellationToken)
    {
        _ = await events.PublishAsync(AppEventKind.GuessingChanged, cancellationToken);
        foreach (var observer in observers)
        {
            await observer.GuessingChangedAsync(hostId, cancellationToken);
        }
    }
}
