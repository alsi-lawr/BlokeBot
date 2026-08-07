using System.Text.Json.Serialization;

namespace BlokeBot.Twitch.Runtime;

internal sealed record EventSubStreamOnlineWireEvent
{
    [JsonPropertyName("id")]
    public string StreamId { get; init; } = string.Empty;

    [JsonPropertyName("broadcaster_user_id")]
    public string BroadcasterUserId { get; init; } = string.Empty;

    [JsonPropertyName("broadcaster_user_login")]
    public string BroadcasterUserLogin { get; init; } = string.Empty;

    [JsonPropertyName("broadcaster_user_name")]
    public string BroadcasterUserName { get; init; } = string.Empty;

    [JsonPropertyName("type")]
    public string StreamType { get; init; } = string.Empty;

    [JsonPropertyName("started_at")]
    public DateTimeOffset? StartedAt { get; init; }

    internal EventSubStreamOnlineEvent? ToDomain(EventSubMetadata metadata) =>
        !EventSubWireIdentity.IsUsable(metadata, BroadcasterUserId, BroadcasterUserLogin)
        || string.IsNullOrWhiteSpace(StreamId)
        || StartedAt is not { } startedAt
            ? null
            : new(
                metadata.MessageId,
                metadata.MessageTimestamp!.Value,
                BroadcasterUserId,
                BroadcasterUserLogin,
                BroadcasterUserName,
                StreamId,
                StreamType,
                startedAt
            );
}

internal sealed record EventSubStreamOfflineWireEvent
{
    [JsonPropertyName("broadcaster_user_id")]
    public string BroadcasterUserId { get; init; } = string.Empty;

    [JsonPropertyName("broadcaster_user_login")]
    public string BroadcasterUserLogin { get; init; } = string.Empty;

    [JsonPropertyName("broadcaster_user_name")]
    public string BroadcasterUserName { get; init; } = string.Empty;

    internal EventSubStreamOfflineEvent? ToDomain(EventSubMetadata metadata) =>
        !EventSubWireIdentity.IsUsable(metadata, BroadcasterUserId, BroadcasterUserLogin)
            ? null
            : new(
                metadata.MessageId,
                metadata.MessageTimestamp!.Value,
                BroadcasterUserId,
                BroadcasterUserLogin,
                BroadcasterUserName
            );
}

internal sealed record EventSubFollowWireEvent
{
    [JsonPropertyName("user_id")]
    public string UserId { get; init; } = string.Empty;

    [JsonPropertyName("user_login")]
    public string UserLogin { get; init; } = string.Empty;

    [JsonPropertyName("user_name")]
    public string UserName { get; init; } = string.Empty;

    [JsonPropertyName("broadcaster_user_id")]
    public string BroadcasterUserId { get; init; } = string.Empty;

    [JsonPropertyName("broadcaster_user_login")]
    public string BroadcasterUserLogin { get; init; } = string.Empty;

    [JsonPropertyName("broadcaster_user_name")]
    public string BroadcasterUserName { get; init; } = string.Empty;

    [JsonPropertyName("followed_at")]
    public DateTimeOffset? FollowedAt { get; init; }

    internal EventSubFollowEvent? ToDomain(EventSubMetadata metadata) =>
        !EventSubWireIdentity.IsUsable(metadata, BroadcasterUserId, BroadcasterUserLogin)
        || string.IsNullOrWhiteSpace(UserId)
        || string.IsNullOrWhiteSpace(UserLogin)
            ? null
            : new(
                metadata.MessageId,
                metadata.MessageTimestamp!.Value,
                UserId,
                UserLogin,
                UserName,
                BroadcasterUserId,
                BroadcasterUserLogin,
                BroadcasterUserName,
                FollowedAt ?? metadata.MessageTimestamp!.Value
            );
}

internal sealed record EventSubSubscriptionWireEvent
{
    [JsonPropertyName("user_id")]
    public string UserId { get; init; } = string.Empty;

    [JsonPropertyName("user_login")]
    public string UserLogin { get; init; } = string.Empty;

    [JsonPropertyName("user_name")]
    public string UserName { get; init; } = string.Empty;

    [JsonPropertyName("broadcaster_user_id")]
    public string BroadcasterUserId { get; init; } = string.Empty;

    [JsonPropertyName("broadcaster_user_login")]
    public string BroadcasterUserLogin { get; init; } = string.Empty;

    [JsonPropertyName("broadcaster_user_name")]
    public string BroadcasterUserName { get; init; } = string.Empty;

    [JsonPropertyName("tier")]
    public string Tier { get; init; } = string.Empty;

    [JsonPropertyName("is_gift")]
    public bool IsGift { get; init; }

    internal EventSubSubscriptionEvent? ToDomain(EventSubMetadata metadata) =>
        !EventSubWireIdentity.IsUsable(metadata, BroadcasterUserId, BroadcasterUserLogin)
        || string.IsNullOrWhiteSpace(UserId)
        || string.IsNullOrWhiteSpace(UserLogin)
            ? null
            : new(
                metadata.MessageId,
                metadata.MessageTimestamp!.Value,
                UserId,
                UserLogin,
                UserName,
                BroadcasterUserId,
                BroadcasterUserLogin,
                BroadcasterUserName,
                Tier,
                IsGift
            );
}

internal sealed record EventSubSubscriptionGiftWireEvent
{
    [JsonPropertyName("user_id")]
    public string? UserId { get; init; }

    [JsonPropertyName("user_login")]
    public string? UserLogin { get; init; }

    [JsonPropertyName("user_name")]
    public string? UserName { get; init; }

    [JsonPropertyName("broadcaster_user_id")]
    public string BroadcasterUserId { get; init; } = string.Empty;

    [JsonPropertyName("broadcaster_user_login")]
    public string BroadcasterUserLogin { get; init; } = string.Empty;

    [JsonPropertyName("broadcaster_user_name")]
    public string BroadcasterUserName { get; init; } = string.Empty;

    [JsonPropertyName("total")]
    public int? Total { get; init; }

    [JsonPropertyName("tier")]
    public string Tier { get; init; } = string.Empty;

    [JsonPropertyName("is_anonymous")]
    public bool IsAnonymous { get; init; }

    internal EventSubSubscriptionGiftEvent? ToDomain(EventSubMetadata metadata) =>
        !EventSubWireIdentity.IsUsable(metadata, BroadcasterUserId, BroadcasterUserLogin)
        || Total is not { } total
        || total < 1
        || (!IsAnonymous && string.IsNullOrWhiteSpace(UserId))
            ? null
            : new(
                metadata.MessageId,
                metadata.MessageTimestamp!.Value,
                IsAnonymous ? null : UserId,
                IsAnonymous ? null : UserLogin,
                IsAnonymous ? null : UserName,
                BroadcasterUserId,
                BroadcasterUserLogin,
                BroadcasterUserName,
                total,
                Tier,
                IsAnonymous
            );
}

internal sealed record EventSubCheerWireEvent
{
    [JsonPropertyName("is_anonymous")]
    public bool IsAnonymous { get; init; }

    [JsonPropertyName("user_id")]
    public string? UserId { get; init; }

    [JsonPropertyName("user_login")]
    public string? UserLogin { get; init; }

    [JsonPropertyName("user_name")]
    public string? UserName { get; init; }

    [JsonPropertyName("broadcaster_user_id")]
    public string BroadcasterUserId { get; init; } = string.Empty;

    [JsonPropertyName("broadcaster_user_login")]
    public string BroadcasterUserLogin { get; init; } = string.Empty;

    [JsonPropertyName("broadcaster_user_name")]
    public string BroadcasterUserName { get; init; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    [JsonPropertyName("bits")]
    public int? Bits { get; init; }

    internal EventSubCheerEvent? ToDomain(EventSubMetadata metadata) =>
        !EventSubWireIdentity.IsUsable(metadata, BroadcasterUserId, BroadcasterUserLogin)
        || Bits is not { } bits
        || bits < 1
        || (!IsAnonymous && string.IsNullOrWhiteSpace(UserId))
            ? null
            : new(
                metadata.MessageId,
                metadata.MessageTimestamp!.Value,
                IsAnonymous ? null : UserId,
                IsAnonymous ? null : UserLogin,
                IsAnonymous ? null : UserName,
                BroadcasterUserId,
                BroadcasterUserLogin,
                BroadcasterUserName,
                bits,
                Message,
                IsAnonymous
            );
}

internal sealed record EventSubHypeTrainWireEvent
{
    [JsonPropertyName("broadcaster_user_id")]
    public string BroadcasterUserId { get; init; } = string.Empty;

    [JsonPropertyName("broadcaster_user_login")]
    public string BroadcasterUserLogin { get; init; } = string.Empty;

    [JsonPropertyName("broadcaster_user_name")]
    public string BroadcasterUserName { get; init; } = string.Empty;

    [JsonPropertyName("level")]
    public int? Level { get; init; }

    [JsonPropertyName("total")]
    public int? Total { get; init; }

    internal EventSubHypeTrainEvent? ToDomain(
        EventSubHypeTrainStage stage,
        EventSubMetadata metadata
    ) =>
        !EventSubWireIdentity.IsUsable(metadata, BroadcasterUserId, BroadcasterUserLogin)
            ? null
            : new(
                metadata.MessageId,
                metadata.MessageTimestamp!.Value,
                stage,
                BroadcasterUserId,
                BroadcasterUserLogin,
                BroadcasterUserName,
                Level ?? 0,
                Total ?? 0
            );
}

internal sealed record EventSubChatNotificationWireEvent
{
    [JsonPropertyName("broadcaster_user_id")]
    public string BroadcasterUserId { get; init; } = string.Empty;

    [JsonPropertyName("broadcaster_user_login")]
    public string BroadcasterUserLogin { get; init; } = string.Empty;

    [JsonPropertyName("broadcaster_user_name")]
    public string BroadcasterUserName { get; init; } = string.Empty;

    [JsonPropertyName("chatter_user_id")]
    public string? ChatterUserId { get; init; }

    [JsonPropertyName("chatter_user_login")]
    public string? ChatterUserLogin { get; init; }

    [JsonPropertyName("chatter_user_name")]
    public string? ChatterUserName { get; init; }

    [JsonPropertyName("chatter_is_anonymous")]
    public bool ChatterIsAnonymous { get; init; }

    [JsonPropertyName("notice_type")]
    public string NoticeType { get; init; } = string.Empty;

    [JsonPropertyName("system_message")]
    public string SystemMessage { get; init; } = string.Empty;

    [JsonPropertyName("message")]
    public EventSubChatNotificationWireMessage? Message { get; init; }

    internal EventSubChatNotificationEvent? ToDomain(EventSubMetadata metadata) =>
        !EventSubWireIdentity.IsUsable(metadata, BroadcasterUserId, BroadcasterUserLogin)
        || string.IsNullOrWhiteSpace(NoticeType)
            ? null
            : new(
                metadata.MessageId,
                metadata.MessageTimestamp!.Value,
                BroadcasterUserId,
                BroadcasterUserLogin,
                BroadcasterUserName,
                ChatterIsAnonymous ? null : ChatterUserId,
                ChatterIsAnonymous ? null : ChatterUserLogin,
                ChatterIsAnonymous ? null : ChatterUserName,
                ChatterIsAnonymous,
                NoticeType,
                SystemMessage,
                Message?.Text ?? string.Empty
            );
}

internal sealed record EventSubChatNotificationWireMessage
{
    [JsonPropertyName("text")]
    public string Text { get; init; } = string.Empty;
}

internal static class EventSubWireIdentity
{
    internal static bool IsUsable(
        EventSubMetadata metadata,
        string broadcasterUserId,
        string broadcasterUserLogin
    ) =>
        !string.IsNullOrWhiteSpace(metadata.MessageId)
        && metadata.MessageTimestamp is { } timestamp
        && timestamp != default
        && !string.IsNullOrWhiteSpace(broadcasterUserId)
        && !string.IsNullOrWhiteSpace(broadcasterUserLogin);
}
