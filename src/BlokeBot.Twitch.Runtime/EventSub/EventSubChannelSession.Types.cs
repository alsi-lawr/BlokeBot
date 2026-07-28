namespace BlokeBot.Twitch.Runtime;

internal enum EventSubChannelReconciliationTarget
{
    Present,
    Absent,
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

    internal required string AccessToken { get; init; }

    internal required EventSubSubscriptionReadiness Readiness { get; init; }

    internal IReadOnlyList<string> AdditionalSubscriptionIds { get; init; } = [];

    internal EventSubOperationSubscriptionState ShoutoutSubscriptions { get; init; } =
        new EventSubOperationSubscriptionState.NotConfigured();

    internal EventSubOperationSubscriptionState PollSubscriptions { get; init; } =
        new EventSubOperationSubscriptionState.NotConfigured();

    internal EventSubOperationSubscriptionState RewardRedemptionSubscriptions { get; init; } =
        new EventSubOperationSubscriptionState.NotConfigured();

    internal EventSubOperationSubscriptionState PredictionSubscriptions { get; init; } =
        new EventSubOperationSubscriptionState.NotConfigured();
}

internal enum EventSubOperationSubscriptionKind
{
    Shoutouts,
    Polls,
    RewardRedemptions,
    Predictions,
}
