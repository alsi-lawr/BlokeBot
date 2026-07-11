using BlokeBot.Eventing;

namespace BlokeBot.Twitch.Runtime;

public static class TwitchBotObserverPolicyKeys
{
    public static ObserverFailurePolicyKey IrcMessages { get; } =
        ObserverFailurePolicyKey.Named("TwitchBot.Irc.Messages");

    public static ObserverFailurePolicyKey EventSubMessages { get; } =
        ObserverFailurePolicyKey.Named("TwitchBot.EventSub.Messages");

    public static ObserverFailurePolicyKey OutboundQueueAlerts { get; } =
        ObserverFailurePolicyKey.Named("TwitchBot.PublicChat.QueueAlerts");
}
