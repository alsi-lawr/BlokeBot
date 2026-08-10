namespace BlokeBot.Twitch.Runtime;

/// <summary>
/// Receives the bounded Twitch EventSub deliveries that can start automation flows. Implementations
/// own host resolution, feature gating, and duplicate suppression; the transport acknowledges
/// Twitch before these observers run and never depends on their outcome.
/// </summary>
public interface ITwitchEventAutomationObserver
{
    Task StreamOnlineAsync(EventSubStreamOnlineEvent streamOnline, CancellationToken cancellation);

    Task StreamOfflineAsync(
        EventSubStreamOfflineEvent streamOffline,
        CancellationToken cancellation
    );

    Task FollowReceivedAsync(EventSubFollowEvent follow, CancellationToken cancellation);

    Task SubscriptionReceivedAsync(
        EventSubSubscriptionEvent subscription,
        CancellationToken cancellation
    );

    Task SubscriptionGiftReceivedAsync(
        EventSubSubscriptionGiftEvent gift,
        CancellationToken cancellation
    );

    Task CheerReceivedAsync(EventSubCheerEvent cheer, CancellationToken cancellation);

    Task IncomingRaidReceivedAsync(
        EventSubIncomingRaidEvent incomingRaid,
        CancellationToken cancellation
    );

    Task HypeTrainChangedAsync(EventSubHypeTrainEvent hypeTrain, CancellationToken cancellation);

    Task ChatNotificationReceivedAsync(
        EventSubChatNotificationEvent notification,
        CancellationToken cancellation
    );

    Task RewardRedemptionReceivedAsync(
        EventSubRewardRedemptionEvent redemption,
        CancellationToken cancellation
    );

    Task ShoutoutOccurredAsync(EventSubShoutoutEvent shoutout, CancellationToken cancellation);

    Task PollChangedAsync(EventSubPollEvent poll, CancellationToken cancellation);

    Task PredictionChangedAsync(EventSubPredictionEvent prediction, CancellationToken cancellation);
}

/// <summary>
/// Answers whether a channel currently needs an automation EventSub subscription group. The
/// automation feature owner implements this so subscription lifecycle follows host connections and
/// enabled flows.
/// </summary>
public interface IEventSubRequirementSource
{
    ValueTask<bool> RequiresAsync(
        string channel,
        AutomationEventSubRequirement requirement,
        CancellationToken cancellation
    );
}

public interface IAutomationEventSubRequirementSource : IEventSubRequirementSource;

public enum AutomationEventSubRequirement
{
    Stream,
    Follows,
    Subscriptions,
    Cheers,
    HypeTrain,
    ChatNotifications,
    IncomingRaids,

    /// <summary>
    /// The redemption EventSub subscription lifecycle is owned by the Rewards &amp; redemptions
    /// feature, which subscribes whenever that feature is enabled. This requirement exists for
    /// automation diagnostics metadata and is never consulted for subscription creation.
    /// </summary>
    Redemptions,

    /// <summary>
    /// Subscription lifecycle owned by the Shoutouts feature; diagnostics metadata only, never
    /// consulted for subscription creation.
    /// </summary>
    Shoutouts,

    /// <summary>
    /// Subscription lifecycle owned by the Polls feature; diagnostics metadata only, never
    /// consulted for subscription creation.
    /// </summary>
    Polls,

    /// <summary>
    /// Subscription lifecycle owned by the Predictions feature; diagnostics metadata only, never
    /// consulted for subscription creation.
    /// </summary>
    Predictions,
}

public sealed record EventSubStreamOnlineEvent(
    string MessageId,
    DateTimeOffset MessageTimestamp,
    string BroadcasterUserId,
    string BroadcasterUserLogin,
    string BroadcasterUserName,
    string StreamId,
    string StreamType,
    DateTimeOffset StartedAt
);

public sealed record EventSubStreamOfflineEvent(
    string MessageId,
    DateTimeOffset MessageTimestamp,
    string BroadcasterUserId,
    string BroadcasterUserLogin,
    string BroadcasterUserName
);

public sealed record EventSubFollowEvent(
    string MessageId,
    DateTimeOffset MessageTimestamp,
    string UserId,
    string UserLogin,
    string UserName,
    string BroadcasterUserId,
    string BroadcasterUserLogin,
    string BroadcasterUserName,
    DateTimeOffset FollowedAt
);

public sealed record EventSubSubscriptionEvent(
    string MessageId,
    DateTimeOffset MessageTimestamp,
    string UserId,
    string UserLogin,
    string UserName,
    string BroadcasterUserId,
    string BroadcasterUserLogin,
    string BroadcasterUserName,
    string Tier,
    bool IsGift
);

public sealed record EventSubSubscriptionGiftEvent(
    string MessageId,
    DateTimeOffset MessageTimestamp,
    string? UserId,
    string? UserLogin,
    string? UserName,
    string BroadcasterUserId,
    string BroadcasterUserLogin,
    string BroadcasterUserName,
    int Total,
    string Tier,
    bool IsAnonymous
);

public sealed record EventSubCheerEvent(
    string MessageId,
    DateTimeOffset MessageTimestamp,
    string? UserId,
    string? UserLogin,
    string? UserName,
    string BroadcasterUserId,
    string BroadcasterUserLogin,
    string BroadcasterUserName,
    int Bits,
    string Message,
    bool IsAnonymous
);

public enum EventSubHypeTrainStage
{
    Begin,
    Progress,
    End,
}

public sealed record EventSubHypeTrainEvent(
    string MessageId,
    DateTimeOffset MessageTimestamp,
    EventSubHypeTrainStage Stage,
    string BroadcasterUserId,
    string BroadcasterUserLogin,
    string BroadcasterUserName,
    int Level,
    int Total
);

public sealed record EventSubChatNotificationEvent(
    string MessageId,
    DateTimeOffset MessageTimestamp,
    string BroadcasterUserId,
    string BroadcasterUserLogin,
    string BroadcasterUserName,
    string? ChatterUserId,
    string? ChatterUserLogin,
    string? ChatterUserName,
    bool ChatterIsAnonymous,
    string NoticeType,
    string SystemMessage,
    string MessageText
);
