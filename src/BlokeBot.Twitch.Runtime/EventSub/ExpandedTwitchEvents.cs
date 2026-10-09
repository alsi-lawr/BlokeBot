using System.Collections.Immutable;

namespace BlokeBot.Twitch.Runtime;

public interface IExpandedTwitchEventObserver
{
    Task StreamEndedAsync(EventSubStreamOfflineEvent stream, CancellationToken cancellation);
    Task AdBreakStartedAsync(EventSubAdBreakEvent ad, CancellationToken cancellation);
    Task GoalChangedAsync(EventSubGoalEvent goal, CancellationToken cancellation);
    Task ChatSettingsChangedAsync(
        EventSubChatSettingsEvent settings,
        CancellationToken cancellation
    );
    Task ModerationOccurredAsync(
        EventSubModerationEvent moderation,
        CancellationToken cancellation
    );
    Task ChatReceivedAsync(EventSubObservedChatEvent message, CancellationToken cancellation);
    Task OutgoingRaidAsync(EventSubIncomingRaidEvent raid, CancellationToken cancellation);
    Task MetadataChangedAsync(EventSubChannelUpdateEvent metadata, CancellationToken cancellation);
    Task RedemptionChangedAsync(
        EventSubRewardRedemptionEvent redemption,
        CancellationToken cancellation
    );
}

public sealed record EventSubAdBreakEvent(
    string MessageId,
    DateTimeOffset Timestamp,
    string BroadcasterId,
    DateTimeOffset StartedAt,
    int DurationSeconds,
    bool IsAutomatic
);

public enum EventSubGoalStage
{
    Begin,
    Progress,
    End,
}

public sealed record EventSubGoalEvent(
    string MessageId,
    DateTimeOffset Timestamp,
    string BroadcasterId,
    string GoalId,
    string Type,
    EventSubGoalStage Stage,
    long CurrentAmount,
    long TargetAmount,
    bool Achieved
);

public sealed record EventSubChatSettingsEvent(
    string MessageId,
    DateTimeOffset Timestamp,
    string BroadcasterId,
    bool Slow,
    int SlowSeconds,
    bool Subscribers,
    bool Emotes,
    bool Followers,
    int FollowerMinutes,
    bool UniqueChat
);

public sealed record EventSubModerationEvent(
    string MessageId,
    DateTimeOffset Timestamp,
    string BroadcasterId,
    string SourceBroadcasterId,
    string SourceBroadcasterLogin,
    string Action,
    string ModeratorId,
    string ModeratorLogin,
    string ModeratorName,
    string SubjectLogin,
    string SubjectName,
    int DurationSeconds,
    int AffectedCount
);

public sealed record EventSubObservedChatEvent(
    string MessageId,
    DateTimeOffset Timestamp,
    string BroadcasterId,
    string SourceBroadcasterId,
    string ViewerId,
    string ViewerLogin,
    string ViewerName,
    string Text,
    ImmutableArray<string> EmoteIds
);

public static class EventSubModerationActions
{
    public static ImmutableArray<string> All { get; } =
    [
        "ban",
        "timeout",
        "unban",
        "untimeout",
        "clear",
        "emoteonly",
        "emoteonlyoff",
        "followers",
        "followersoff",
        "uniquechat",
        "uniquechatoff",
        "slow",
        "slowoff",
        "subscribers",
        "subscribersoff",
        "unraid",
        "delete",
        "unvip",
        "vip",
        "raid",
        "add_blocked_term",
        "add_permitted_term",
        "remove_blocked_term",
        "remove_permitted_term",
        "mod",
        "unmod",
        "approve_unban_request",
        "deny_unban_request",
        "warn",
        "shared_chat_ban",
        "shared_chat_timeout",
        "shared_chat_unban",
        "shared_chat_untimeout",
        "shared_chat_delete",
    ];
    public static ImmutableArray<string> ReadScopes { get; } =
    [
        "moderator:read:blocked_terms",
        "moderator:read:chat_settings",
        "moderator:read:unban_requests",
        "moderator:read:banned_users",
        "moderator:read:chat_messages",
        "moderator:read:warnings",
        "moderator:read:moderators",
        "moderator:read:vips",
    ];

    public static bool ScopeSatisfied(IEnumerable<string> granted, string required)
    {
        var scopes = granted.ToHashSet(StringComparer.Ordinal);
        return scopes.Contains(required)
            || required switch
            {
                "moderator:read:blocked_terms" => scopes.Contains("moderator:manage:blocked_terms"),
                "moderator:read:chat_settings" => scopes.Contains("moderator:manage:chat_settings"),
                "moderator:read:unban_requests" => scopes.Contains(
                    "moderator:manage:unban_requests"
                ),
                "moderator:read:banned_users" => scopes.Contains("moderator:manage:banned_users"),
                "moderator:read:chat_messages" => scopes.Contains("moderator:manage:chat_messages"),
                "moderator:read:warnings" => scopes.Contains("moderator:manage:warnings"),
                _ => false,
            };
    }
}
