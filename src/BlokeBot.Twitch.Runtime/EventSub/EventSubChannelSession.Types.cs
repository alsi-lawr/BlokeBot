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

internal sealed record BroadcasterPollSubscriptionGroup(
    string SubscriptionId,
    IReadOnlyList<string> AdditionalSubscriptionIds
)
{
    internal static BroadcasterPollSubscriptionGroup From(ActiveEventSubSubscription subscription)
    {
        return new(subscription.SubscriptionId, subscription.AdditionalSubscriptionIds);
    }
}

internal abstract record BroadcasterPollSubscriptionState
{
    private BroadcasterPollSubscriptionState() { }

    internal sealed record NotConfigured : BroadcasterPollSubscriptionState;

    internal sealed record Unavailable(AccessTokenUnavailableReason Reason)
        : BroadcasterPollSubscriptionState;

    internal sealed record Active(BroadcasterPollSubscriptionGroup Group)
        : BroadcasterPollSubscriptionState;

    internal sealed record CleanupPending(BroadcasterPollSubscriptionGroup Group)
        : BroadcasterPollSubscriptionState;
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

    internal BroadcasterPollSubscriptionState PollSubscriptions { get; init; } =
        new BroadcasterPollSubscriptionState.NotConfigured();
}
