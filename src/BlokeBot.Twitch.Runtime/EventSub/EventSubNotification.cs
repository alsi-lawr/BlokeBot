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
    ) =>
        envelope.Payload.Event is not { } payload
            ? new Unknown()
            : envelope.Metadata.SubscriptionType switch
            {
                "" or "channel.chat.message" => payload.Deserialize<EventSubChatMessageEvent>(
                    options
                )
                    is { } chat
                    ? new Chat(chat)
                    : new Unknown(),
                "channel.shoutout.create" => payload.Deserialize<EventSubShoutoutWireEvent>(options)
                    is { } shoutout
                    ? new Shoutout(
                        shoutout.ToDomain(
                            EventSubShoutoutDirection.Sent,
                            envelope.Metadata.MessageId
                        )
                    )
                    : new Unknown(),
                "channel.shoutout.receive" => payload.Deserialize<EventSubShoutoutWireEvent>(
                    options
                )
                    is { } shoutout
                    ? new Shoutout(
                        shoutout.ToDomain(
                            EventSubShoutoutDirection.Received,
                            envelope.Metadata.MessageId
                        )
                    )
                    : new Unknown(),
                "channel.raid" when envelope.Metadata.SubscriptionVersion == "1" =>
                    ParseIncomingRaid(payload, options, envelope.Metadata),
                "channel.poll.begin" or "channel.poll.progress" or "channel.poll.end" =>
                    payload.Deserialize<EventSubPollWireEvent>(options) is { } poll
                        ? new Poll(poll.ToDomain(envelope.Metadata.MessageId))
                        : new Unknown(),
                "channel.prediction.begin"
                or "channel.prediction.progress"
                or "channel.prediction.lock"
                or "channel.prediction.end" => payload.Deserialize<EventSubPredictionWireEvent>(
                    options
                )
                    is { } prediction
                    ? ParsePrediction(prediction, envelope.Metadata)
                    : new Unknown(),
                "channel.channel_points_custom_reward_redemption.add"
                or "channel.channel_points_custom_reward_redemption.update" =>
                    payload.Deserialize<EventSubRewardRedemptionWireEvent>(options)
                        is { } redemption
                        ? new RewardRedemption(redemption.ToDomain(envelope.Metadata.MessageId))
                        : new Unknown(),
                _ => new Unknown(),
            };

    private static EventSubNotification ParseIncomingRaid(
        JsonElement payload,
        JsonSerializerOptions options,
        EventSubMetadata metadata
    ) =>
        payload.Deserialize<EventSubIncomingRaidWireEvent>(options) switch
        {
            { } incomingRaid when incomingRaid.ToDomain(metadata) is { } normalized =>
                new IncomingRaid(normalized),
            _ => new Unknown(),
        };

    private static EventSubNotification ParsePrediction(
        EventSubPredictionWireEvent prediction,
        EventSubMetadata metadata
    ) =>
        prediction.ToDomain(metadata.SubscriptionType, metadata.MessageId) is { } normalized
            ? new Prediction(normalized)
            : new Unknown();
}
