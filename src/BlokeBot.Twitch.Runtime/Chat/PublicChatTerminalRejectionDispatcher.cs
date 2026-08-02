using BlokeBot.Eventing;

namespace BlokeBot.Twitch.Runtime;

internal sealed class PublicChatTerminalRejectionDispatcher(
    IEnumerable<IPublicChatTerminalRejectionObserver> observers,
    ObserverFanOut<
        PublicChatTerminalRejectionObserverBoundary,
        PublicChatTerminalRejection,
        PublicChatTerminalRejectionDeadLetter
    > fanOut
)
{
    private static readonly ObserverEventIdentity _rejectionEvent = ObserverEventIdentity.Named(
        "PublicChatTerminalRejection"
    );
    private readonly IPublicChatTerminalRejectionObserver[] _observers = [.. observers];

    public bool HasObservers => _observers.Length > 0;

    public async Task NotifyAsync(
        PublicChatTerminalRejection rejection,
        CancellationToken cancellationToken
    ) =>
        _ = await fanOut.DispatchAsync(
            _observers,
            _ => new ObserverDispatch<
                PublicChatTerminalRejection,
                PublicChatTerminalRejectionDeadLetter
            >
            {
                Event = rejection,
                EventIdentity = _rejectionEvent,
                DeadLetter = new PublicChatTerminalRejectionDeadLetter(
                    rejection.Channel,
                    rejection.ProviderCode
                ),
            },
            observer => ObserverIdentity.For(observer.GetType()),
            static (observer, value, token) => observer.TerminalRejectionAsync(value, token),
            cancellationToken
        );
}
