namespace BlokeBot.Twitch.Runtime;

internal abstract partial record EventSubNotification
{
    internal static bool IsTypedSubscription(string subscriptionType) =>
        subscriptionType
            is ""
                or "channel.chat.message"
                or "channel.shoutout.create"
                or "channel.shoutout.receive"
                or "channel.raid"
                or "channel.poll.begin"
                or "channel.poll.progress"
                or "channel.poll.end"
                or "channel.prediction.begin"
                or "channel.prediction.progress"
                or "channel.prediction.lock"
                or "channel.prediction.end"
                or "channel.channel_points_custom_reward_redemption.add"
                or "channel.channel_points_custom_reward_redemption.update"
                or "stream.online"
                or "stream.offline"
                or "channel.update"
                or "channel.follow"
                or "channel.subscribe"
                or "channel.subscription.gift"
                or "channel.cheer"
                or "channel.hype_train.begin"
                or "channel.hype_train.progress"
                or "channel.hype_train.end"
                or "channel.chat.notification";
}
