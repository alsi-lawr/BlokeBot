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

    internal sealed record StreamOnline(EventSubStreamOnlineEvent Event) : EventSubNotification;

    internal sealed record StreamOffline(EventSubStreamOfflineEvent Event) : EventSubNotification;

    internal sealed record ChannelUpdate(EventSubChannelUpdateEvent Event) : EventSubNotification;

    internal sealed record Follow(EventSubFollowEvent Event) : EventSubNotification;

    internal sealed record Subscription(EventSubSubscriptionEvent Event) : EventSubNotification;

    internal sealed record SubscriptionGift(EventSubSubscriptionGiftEvent Event)
        : EventSubNotification;

    internal sealed record Cheer(EventSubCheerEvent Event) : EventSubNotification;

    internal sealed record HypeTrain(EventSubHypeTrainEvent Event) : EventSubNotification;

    internal sealed record ChatNotification(EventSubChatNotificationEvent Event)
        : EventSubNotification;

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

        var subscriptionType = envelope.Metadata.SubscriptionType;
        var subscriptionVersion = envelope.Metadata.SubscriptionVersion;
        if (envelope.Subscription is { ValueKind: JsonValueKind.Object } subscription)
        {
            subscriptionType =
                subscriptionType.Length > 0 ? subscriptionType
                : subscription.TryGetProperty("type", out var type)
                    ? type.GetString() ?? string.Empty
                : string.Empty;
            subscriptionVersion =
                subscriptionVersion.Length > 0 ? subscriptionVersion
                : subscription.TryGetProperty("version", out var version)
                    ? version.GetString() ?? string.Empty
                : string.Empty;
        }

        return subscriptionType switch
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
            "channel.raid" when subscriptionVersion == "1" => ParseIncomingRaid(
                payload,
                options,
                envelope.Metadata,
                envelope.Subscription
            ),
            "channel.poll.begin" or "channel.poll.progress" or "channel.poll.end" =>
                payload.Deserialize<EventSubPollWireEvent>(options) is { } poll
                    ? new Poll(
                        poll.ToDomain(PollStage(subscriptionType), envelope.Metadata.MessageId)
                    )
                    : new Unknown(),
            "channel.prediction.begin"
            or "channel.prediction.progress"
            or "channel.prediction.lock"
            or "channel.prediction.end" => payload.Deserialize<EventSubPredictionWireEvent>(options)
                is { } prediction
                ? ParsePrediction(prediction, envelope.Metadata)
                : new Unknown(),
            "channel.channel_points_custom_reward_redemption.add"
            or "channel.channel_points_custom_reward_redemption.update" =>
                payload.Deserialize<EventSubRewardRedemptionWireEvent>(options) is { } redemption
                    ? new RewardRedemption(
                        redemption.ToDomain(
                            envelope.Metadata.MessageId,
                            subscriptionType
                                == "channel.channel_points_custom_reward_redemption.add"
                        )
                    )
                    : new Unknown(),
            "stream.online" => payload.Deserialize<EventSubStreamOnlineWireEvent>(options)
                is { } streamOnline
                ? Normalized(
                    streamOnline.ToDomain(envelope.Metadata),
                    static value => new StreamOnline(value)
                )
                : new Unknown(),
            "stream.offline" => payload.Deserialize<EventSubStreamOfflineWireEvent>(options)
                is { } streamOffline
                ? Normalized(
                    streamOffline.ToDomain(envelope.Metadata),
                    static value => new StreamOffline(value)
                )
                : new Unknown(),
            "channel.update" when subscriptionVersion == "2" =>
                payload.Deserialize<EventSubChannelUpdateWireEvent>(options) is { } channelUpdate
                    ? Normalized(
                        channelUpdate.ToDomain(envelope.Metadata),
                        static value => new ChannelUpdate(value)
                    )
                    : new Unknown(),
            "channel.follow" => payload.Deserialize<EventSubFollowWireEvent>(options) is { } follow
                ? Normalized(follow.ToDomain(envelope.Metadata), static value => new Follow(value))
                : new Unknown(),
            "channel.subscribe" => payload.Deserialize<EventSubSubscriptionWireEvent>(options)
                is { } subscriber
                ? Normalized(
                    subscriber.ToDomain(envelope.Metadata),
                    static value => new Subscription(value)
                )
                : new Unknown(),
            "channel.subscription.gift" => payload.Deserialize<EventSubSubscriptionGiftWireEvent>(
                options
            )
                is { } gift
                ? Normalized(
                    gift.ToDomain(envelope.Metadata),
                    static value => new SubscriptionGift(value)
                )
                : new Unknown(),
            "channel.cheer" => payload.Deserialize<EventSubCheerWireEvent>(options) is { } cheer
                ? Normalized(cheer.ToDomain(envelope.Metadata), static value => new Cheer(value))
                : new Unknown(),
            "channel.hype_train.begin"
            or "channel.hype_train.progress"
            or "channel.hype_train.end" => payload.Deserialize<EventSubHypeTrainWireEvent>(options)
                is { } hypeTrain
                ? Normalized(
                    hypeTrain.ToDomain(HypeTrainStage(subscriptionType), envelope.Metadata),
                    static value => new HypeTrain(value)
                )
                : new Unknown(),
            "channel.chat.notification" => payload.Deserialize<EventSubChatNotificationWireEvent>(
                options
            )
                is { } notification
                ? Normalized(
                    notification.ToDomain(envelope.Metadata),
                    static value => new ChatNotification(value)
                )
                : new Unknown(),
            _ => new Unknown(),
        };
    }

    private static EventSubNotification Normalized<TEvent>(
        TEvent? normalized,
        Func<TEvent, EventSubNotification> create
    )
        where TEvent : class => normalized is null ? new Unknown() : create(normalized);

    private static EventSubPollStage PollStage(string subscriptionType) =>
        subscriptionType switch
        {
            "channel.poll.begin" => EventSubPollStage.Begin,
            "channel.poll.progress" => EventSubPollStage.Progress,
            _ => EventSubPollStage.End,
        };

    private static EventSubHypeTrainStage HypeTrainStage(string subscriptionType) =>
        subscriptionType switch
        {
            "channel.hype_train.begin" => EventSubHypeTrainStage.Begin,
            "channel.hype_train.progress" => EventSubHypeTrainStage.Progress,
            _ => EventSubHypeTrainStage.End,
        };

    private static EventSubNotification ParseIncomingRaid(
        JsonElement payload,
        JsonSerializerOptions options,
        EventSubMetadata metadata,
        JsonElement? subscription
    ) =>
        RaidDirection(subscription, options) switch
        {
            RaidSubscriptionConditionDirection.Incoming => ParseIncomingRaid(
                payload,
                options,
                metadata,
                EventSubRaidSubscriptionDirection.Incoming
            ),
            RaidSubscriptionConditionDirection.Outgoing => ParseIncomingRaid(
                payload,
                options,
                metadata,
                EventSubRaidSubscriptionDirection.Outgoing
            ),
            RaidSubscriptionConditionDirection.Invalid => new Unknown(),
        };

    private static EventSubNotification ParseIncomingRaid(
        JsonElement payload,
        JsonSerializerOptions options,
        EventSubMetadata metadata,
        EventSubRaidSubscriptionDirection direction
    ) =>
        payload.Deserialize<EventSubIncomingRaidWireEvent>(options) switch
        {
            { } incomingRaid when incomingRaid.ToDomain(metadata, direction) is { } normalized =>
                new IncomingRaid(normalized),
            _ => new Unknown(),
        };

    // Twitch echoes both condition keys on every channel.raid notification, with the unused
    // side as an empty string, so only a non-empty value identifies the subscribed direction.
    private static RaidSubscriptionConditionDirection RaidDirection(
        JsonElement? subscription,
        JsonSerializerOptions options
    ) =>
        DeserializeRaidSubscription(subscription, options)?.Condition switch
        {
            { FromBroadcasterUserId: { Length: > 0 }, ToBroadcasterUserId: "" } =>
                RaidSubscriptionConditionDirection.Outgoing,
            { FromBroadcasterUserId: "", ToBroadcasterUserId: { Length: > 0 } } =>
                RaidSubscriptionConditionDirection.Incoming,
            _ => RaidSubscriptionConditionDirection.Invalid,
        };

    private static EventSubRaidSubscriptionWire? DeserializeRaidSubscription(
        JsonElement? subscription,
        JsonSerializerOptions options
    )
    {
        try
        {
            return subscription?.Deserialize<EventSubRaidSubscriptionWire>(options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private enum RaidSubscriptionConditionDirection
    {
        Invalid,
        Incoming,
        Outgoing,
    }

    private static EventSubNotification ParsePrediction(
        EventSubPredictionWireEvent prediction,
        EventSubMetadata metadata
    ) =>
        prediction.ToDomain(metadata.SubscriptionType, metadata.MessageId) is { } normalized
            ? new Prediction(normalized)
            : new Unknown();
}
