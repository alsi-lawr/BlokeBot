using System.Text.Json;

namespace BlokeBot.Twitch.Runtime;

internal abstract record EventSubNotification
{
    private EventSubNotification() { }

    internal sealed record Chat(EventSubChatMessageEvent Event) : EventSubNotification;

    internal sealed record Shoutout(EventSubShoutoutEvent Event) : EventSubNotification;

    internal sealed record IncomingRaid(EventSubIncomingRaidEvent Event) : EventSubNotification;

    internal sealed record Poll(EventSubPollEvent Event) : EventSubNotification;

    internal sealed record RewardRedemption(EventSubRewardRedemptionEvent Event)
        : EventSubNotification;

    internal sealed record Prediction(EventSubPredictionEvent Event) : EventSubNotification;

    internal sealed record Unknown : EventSubNotification;

    internal static EventSubNotification Parse(
        EventSubEnvelope envelope,
        JsonSerializerOptions options
    )
    {
        if (envelope.Payload.Event is not { } payload)
        {
            return new Unknown();
        }
        return envelope.Metadata.SubscriptionType switch
        {
            "" or "channel.chat.message" => payload.Deserialize<EventSubChatMessageEvent>(options)
                is { } chat
                ? new Chat(chat)
                : new Unknown(),
            "channel.shoutout.create" => payload.Deserialize<EventSubShoutoutWireEvent>(options)
                is { } shoutout
                ? new Shoutout(
                    shoutout.ToDomain(EventSubShoutoutDirection.Sent, envelope.Metadata.MessageId)
                )
                : new Unknown(),
            "channel.shoutout.receive" => payload.Deserialize<EventSubShoutoutWireEvent>(options)
                is { } shoutout
                ? new Shoutout(
                    shoutout.ToDomain(
                        EventSubShoutoutDirection.Received,
                        envelope.Metadata.MessageId
                    )
                )
                : new Unknown(),
            "channel.raid" when envelope.Metadata.SubscriptionVersion == "1" => ParseIncomingRaid(
                payload,
                envelope.Metadata,
                options
            ),
            "channel.poll.begin" or "channel.poll.progress" or "channel.poll.end" =>
                payload.Deserialize<EventSubPollWireEvent>(options) is { } poll
                    ? new Poll(poll.ToDomain(envelope.Metadata.MessageId))
                    : new Unknown(),
            "channel.prediction.begin"
            or "channel.prediction.progress"
            or "channel.prediction.lock"
            or "channel.prediction.end" => payload.Deserialize<EventSubPredictionWireEvent>(options)
                is { } prediction
                ? prediction.ToDomain(
                    envelope.Metadata.SubscriptionType,
                    envelope.Metadata.MessageId
                )
                    is { } normalized
                    ? new Prediction(normalized)
                    : new Unknown()
                : new Unknown(),
            "channel.channel_points_custom_reward_redemption.add"
            or "channel.channel_points_custom_reward_redemption.update" =>
                payload.Deserialize<EventSubRewardRedemptionWireEvent>(options) is { } redemption
                    ? new RewardRedemption(redemption.ToDomain(envelope.Metadata.MessageId))
                    : new Unknown(),
            _ => new Unknown(),
        };
    }

    private static EventSubNotification ParseIncomingRaid(
        JsonElement payload,
        EventSubMetadata metadata,
        JsonSerializerOptions options
    )
    {
        try
        {
            return
                payload.Deserialize<EventSubIncomingRaidWireEvent>(options) is { } incomingRaid
                && incomingRaid.ToDomain(metadata) is { } normalized
                ? new IncomingRaid(normalized)
                : new Unknown();
        }
        catch (JsonException)
        {
            return new Unknown();
        }
    }
}
