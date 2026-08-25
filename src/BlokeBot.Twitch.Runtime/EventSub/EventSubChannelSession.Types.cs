using System.Collections.Immutable;

namespace BlokeBot.Twitch.Runtime;

internal enum EventSubChannelReconciliationTarget
{
    Present,
    Absent,
    Replacing,
}

internal enum EventSubChannelDeletionLifecycle
{
    PreserveRuntime,
    StopRuntime,
}

internal sealed class EventSubChannelStatusPublicationException(Exception innerException)
    : Exception("EventSub channel status publication failed.", innerException);

internal enum EventSubSubscriptionReadiness
{
    PendingStartupDelivery,
    PendingLifecycleStart,
    Ready,
}

internal abstract record EventSubOperationSubscriptionState
{
    private EventSubOperationSubscriptionState() { }

    internal sealed record NotConfigured : EventSubOperationSubscriptionState;

    internal sealed record Unavailable(AccessTokenUnavailableReason Reason)
        : EventSubOperationSubscriptionState;

    internal sealed record Active(ActiveEventSubSubscription Subscription)
        : EventSubOperationSubscriptionState;

    internal sealed record CleanupPending(ActiveEventSubSubscription Subscription)
        : EventSubOperationSubscriptionState;
}

internal sealed record ActiveEventSubSubscription
{
    internal required string Channel { get; init; }

    internal required string SubscriptionId { get; init; }

    internal required string BotLogin { get; init; }

    internal EventSubAuthorizationContext Authorization { get; init; } =
        EventSubAuthorizationContext.ConfiguredBotAuthority;

    internal required EventSubSubscriptionReadiness Readiness { get; init; }

    internal IReadOnlyList<string> AdditionalSubscriptionIds { get; init; } = [];

    internal EventSubOperationSubscriptionState ShoutoutSubscriptions { get; init; } =
        new EventSubOperationSubscriptionState.NotConfigured();

    internal EventSubOperationSubscriptionState RaidSubscriptions { get; init; } =
        new EventSubOperationSubscriptionState.NotConfigured();

    internal EventSubOperationSubscriptionState OutgoingRaidSubscriptions { get; init; } =
        new EventSubOperationSubscriptionState.NotConfigured();

    internal EventSubOperationSubscriptionState PollSubscriptions { get; init; } =
        new EventSubOperationSubscriptionState.NotConfigured();

    internal EventSubOperationSubscriptionState RewardRedemptionSubscriptions { get; init; } =
        new EventSubOperationSubscriptionState.NotConfigured();

    internal EventSubOperationSubscriptionState PredictionSubscriptions { get; init; } =
        new EventSubOperationSubscriptionState.NotConfigured();

    internal EventSubOperationSubscriptionState AutomationStreamSubscriptions { get; init; } =
        new EventSubOperationSubscriptionState.NotConfigured();

    internal EventSubOperationSubscriptionState AutomationChannelUpdateSubscriptions { get; init; } =
        new EventSubOperationSubscriptionState.NotConfigured();

    internal EventSubOperationSubscriptionState AutomationFollowSubscriptions { get; init; } =
        new EventSubOperationSubscriptionState.NotConfigured();

    internal EventSubOperationSubscriptionState AutomationSubscriberSubscriptions { get; init; } =
        new EventSubOperationSubscriptionState.NotConfigured();

    internal EventSubOperationSubscriptionState AutomationCheerSubscriptions { get; init; } =
        new EventSubOperationSubscriptionState.NotConfigured();

    internal EventSubOperationSubscriptionState AutomationHypeTrainSubscriptions { get; init; } =
        new EventSubOperationSubscriptionState.NotConfigured();

    internal EventSubOperationSubscriptionState AutomationChatNotificationSubscriptions { get; init; } =
        new EventSubOperationSubscriptionState.NotConfigured();

    internal ImmutableDictionary<
        EventSubExactSubscription,
        EventSubOperationSubscriptionState
    > ExactSubscriptions { get; init; } =
        ImmutableDictionary<EventSubExactSubscription, EventSubOperationSubscriptionState>.Empty;
}

internal enum EventSubOperationSubscriptionKind
{
    Shoutouts,
    Raids,
    OutgoingRaids,
    Polls,
    RewardRedemptions,
    Predictions,
    AutomationStream,
    AutomationChannelUpdates,
    AutomationFollows,
    AutomationSubscriptions,
    AutomationCheers,
    AutomationHypeTrain,
    AutomationChatNotifications,
}
