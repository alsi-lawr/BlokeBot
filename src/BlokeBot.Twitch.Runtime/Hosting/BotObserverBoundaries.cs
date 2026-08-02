using BlokeBot.Eventing;

namespace BlokeBot.Twitch.Runtime;

public static class BotObserverBoundaries
{
    public static ObserverBoundary IrcMessages { get; } =
        ObserverBoundary.Named("TwitchBot.Irc.Messages");

    public static ObserverBoundary EventSubMessages { get; } =
        ObserverBoundary.Named("TwitchBot.EventSub.Messages");

    public static ObserverBoundary PublicChatQueueAlerts { get; } =
        ObserverBoundary.Named("TwitchBot.PublicChat.QueueAlerts");

    public static ObserverBoundary PublicChatTerminalRejections { get; } =
        ObserverBoundary.Named("TwitchBot.PublicChat.TerminalRejections");
}

internal sealed class IrcMessageObserverBoundary;

internal sealed class EventSubMessageObserverBoundary;

internal sealed class PublicChatQueueAlertObserverBoundary;

internal sealed class PublicChatTerminalRejectionObserverBoundary;

internal sealed record ChatObserverDeadLetter(string Channel) : IObserverDeadLetterPayload;

internal sealed record PublicChatQueueAlertDeadLetter(
    string Channel,
    int PendingCount,
    TimeSpan OldestPendingAge,
    DateTimeOffset OldestPendingAt
) : IObserverDeadLetterPayload;

internal sealed record PublicChatTerminalRejectionDeadLetter(string Channel, string ProviderCode)
    : IObserverDeadLetterPayload;
