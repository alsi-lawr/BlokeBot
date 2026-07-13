using BlokeBot.Commands;
using BlokeBot.Eventing;

namespace BlokeBot.Twitch.Runtime;

public static class TwitchBotObserverBoundaries
{
    public static ObserverBoundary IrcMessages { get; } =
        ObserverBoundary.Named("TwitchBot.Irc.Messages");

    public static ObserverBoundary EventSubMessages { get; } =
        ObserverBoundary.Named("TwitchBot.EventSub.Messages");

    public static ObserverBoundary PublicChatQueueAlerts { get; } =
        ObserverBoundary.Named("TwitchBot.PublicChat.QueueAlerts");
}

internal sealed class TwitchIrcMessageObserverBoundary;

internal sealed class EventSubMessageObserverBoundary;

internal sealed class PublicChatQueueAlertObserverBoundary;

internal sealed record TwitchChatObserverDeadLetter(string Channel) : IObserverDeadLetterPayload;

internal sealed record PublicChatQueueAlertDeadLetter(
    string Channel,
    int PendingCount,
    TimeSpan OldestPendingAge,
    DateTimeOffset OldestPendingAt
) : IObserverDeadLetterPayload;
