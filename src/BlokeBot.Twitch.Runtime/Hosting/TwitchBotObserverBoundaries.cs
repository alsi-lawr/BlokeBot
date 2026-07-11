using BlokeBot.Commands;
using BlokeBot.Eventing;

namespace BlokeBot.Twitch.Runtime;

public static class TwitchBotObserverBoundaries
{
    public static ObserverBoundary IrcMessages { get; } =
        ObserverBoundary.Named("TwitchBot.Irc.Messages");

    public static ObserverBoundary EventSubMessages { get; } =
        ObserverBoundary.Named("TwitchBot.EventSub.Messages");

    public static ObserverBoundary OutboundQueueAlerts { get; } =
        ObserverBoundary.Named("TwitchBot.PublicChat.QueueAlerts");
}

internal sealed class TwitchIrcMessageObserverBoundary;

internal sealed class TwitchEventSubMessageObserverBoundary;

internal sealed class TwitchOutboundQueueAlertObserverBoundary;

internal sealed record TwitchChatObserverDeadLetter(string Channel)
    : IObserverDeadLetterPayload;

internal sealed record TwitchOutboundQueueAlertDeadLetter(
    string Channel,
    int PendingCount,
    TimeSpan OldestPendingAge,
    DateTimeOffset OldestPendingAt
) : IObserverDeadLetterPayload;
