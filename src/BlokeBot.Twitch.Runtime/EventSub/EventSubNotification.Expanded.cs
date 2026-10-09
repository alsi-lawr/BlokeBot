using System.Globalization;
using System.Text.Json;

namespace BlokeBot.Twitch.Runtime;

internal abstract partial record EventSubNotification
{
    internal sealed record AdBreak(EventSubAdBreakEvent Event) : EventSubNotification;

    internal sealed record Goal(EventSubGoalEvent Event) : EventSubNotification;

    internal sealed record ChatSettings(EventSubChatSettingsEvent Event) : EventSubNotification;

    internal sealed record Moderation(EventSubModerationEvent Event) : EventSubNotification;

    private static EventSubNotification ParseExpanded(
        string type,
        string version,
        JsonElement payload,
        EventSubMetadata metadata
    )
    {
        if (
            metadata.MessageTimestamp is not { } at
            || at == default
            || string.IsNullOrWhiteSpace(metadata.MessageId)
            || payload.ValueKind != JsonValueKind.Object
            || ReadText(payload, "broadcaster_user_id") is not { Length: > 0 } broadcaster
        )
        {
            return new Unknown();
        }
        switch (type, version)
        {
            case ("channel.ad_break.begin", "1"):
                return
                    ReadTime(payload, "started_at") is { } started
                    && ReadNumber(payload, "duration_seconds") is { } duration
                    && duration is > 0 and <= 3600
                    && ReadBool(payload, "is_automatic") is { } automatic
                    ? new AdBreak(
                        new(metadata.MessageId, at, broadcaster, started, (int)duration, automatic)
                    )
                    : new Unknown();
            case ("channel.goal.begin" or "channel.goal.progress" or "channel.goal.end", "1"):
                return
                    ReadText(payload, "id") is { Length: > 0 } id
                    && ReadNumber(payload, "current_amount") is { } current
                    && current >= 0
                    && ReadNumber(payload, "target_amount") is { } target
                    && target > 0
                    ? new Goal(
                        new(
                            metadata.MessageId,
                            at,
                            broadcaster,
                            id,
                            ReadText(payload, "type"),
                            type == "channel.goal.begin" ? EventSubGoalStage.Begin
                                : type == "channel.goal.progress" ? EventSubGoalStage.Progress
                                : EventSubGoalStage.End,
                            current,
                            target,
                            ReadBool(payload, "is_achieved") ?? false
                        )
                    )
                    : new Unknown();
            case ("channel.chat_settings.update", "1"):
                return
                    ReadSmallNumber(payload, "slow_mode_wait_time_seconds") is { } slowSeconds
                    && ReadSmallNumber(payload, "follower_mode_duration_minutes")
                        is { } followerMinutes
                    && ReadBool(payload, "slow_mode") is { } slow
                    && ReadBool(payload, "subscriber_mode") is { } subscribers
                    && ReadBool(payload, "emote_mode") is { } emotes
                    && ReadBool(payload, "follower_mode") is { } followers
                    && ReadBool(payload, "unique_chat_mode") is { } unique
                    ? new ChatSettings(
                        new(
                            metadata.MessageId,
                            at,
                            broadcaster,
                            slow,
                            slowSeconds,
                            subscribers,
                            emotes,
                            followers,
                            followerMinutes,
                            unique
                        )
                    )
                    : new Unknown();
            case ("channel.moderate", "2"):
                var action = ReadText(payload, "action");
                if (
                    !EventSubModerationActions.All.Contains(action)
                    || ReadText(payload, "moderator_user_id") is not { Length: > 0 }
                    || ReadText(payload, "source_broadcaster_user_id") is not { Length: > 0 } source
                    || !OptionalTexts(
                        payload,
                        "broadcaster_user_login",
                        "broadcaster_user_name",
                        "source_broadcaster_user_login",
                        "source_broadcaster_user_name",
                        "moderator_user_login",
                        "moderator_user_name"
                    )
                )
                {
                    return new Unknown();
                }
                var key =
                    action is "approve_unban_request" or "deny_unban_request" ? "unban_request"
                    : action.Contains("_term", StringComparison.Ordinal) ? "automod_terms"
                    : action
                        is "followers"
                            or "slow"
                            or "vip"
                            or "unvip"
                            or "mod"
                            or "unmod"
                            or "ban"
                            or "unban"
                            or "timeout"
                            or "untimeout"
                            or "raid"
                            or "unraid"
                            or "delete"
                            or "warn"
                            or "shared_chat_ban"
                            or "shared_chat_unban"
                            or "shared_chat_timeout"
                            or "shared_chat_untimeout"
                            or "shared_chat_delete"
                        ? action
                    : null;
                var detail =
                    key is not null
                    && payload.TryGetProperty(key, out var value)
                    && value.ValueKind == JsonValueKind.Object
                        ? value
                        : default;
                if (
                    (
                        key is not null
                        && payload.TryGetProperty(key, out var declared)
                        && declared.ValueKind is not (JsonValueKind.Object or JsonValueKind.Null)
                    ) || !ValidModerationDetail(action, detail)
                )
                {
                    return new Unknown();
                }
                var wait = action == "slow" ? ReadSmallNumber(detail, "wait_time_seconds") ?? 0 : 0;
                var viewers = action == "raid" ? ReadSmallNumber(detail, "viewer_count") ?? 0 : 0;
                var moderationDuration =
                    action is "timeout" or "shared_chat_timeout"
                    && ReadTime(detail, "expires_at") is { } expires
                        ? Math.Clamp((expires - at).TotalSeconds, 0, int.MaxValue)
                        : wait;
                var count =
                    detail.ValueKind == JsonValueKind.Object
                    && key == "automod_terms"
                    && detail.TryGetProperty("terms", out var terms)
                    && terms.ValueKind == JsonValueKind.Array
                        ? terms.GetArrayLength()
                        : viewers;
                return new Moderation(
                    new(
                        metadata.MessageId,
                        at,
                        broadcaster,
                        source,
                        ReadText(payload, "source_broadcaster_user_login"),
                        action,
                        ReadText(payload, "moderator_user_id"),
                        ReadText(payload, "moderator_user_login"),
                        ReadText(payload, "moderator_user_name"),
                        ReadText(detail, "user_login"),
                        ReadText(detail, "user_name"),
                        (int)moderationDuration,
                        count
                    )
                );
            default:
                return new Unknown();
        }
    }

    private static bool ValidModerationDetail(string action, JsonElement detail) =>
        action switch
        {
            "followers" => OptionalSmallNumber(detail, "follow_duration_minutes"),
            "slow" => OptionalSmallNumber(detail, "wait_time_seconds"),
            "vip"
            or "unvip"
            or "mod"
            or "unmod"
            or "unban"
            or "untimeout"
            or "unraid"
            or "shared_chat_unban"
            or "shared_chat_untimeout" => OptionalSubject(detail),
            "ban" or "shared_chat_ban" => OptionalSubject(detail)
                && OptionalTexts(detail, "reason"),
            "timeout" or "shared_chat_timeout" => OptionalSubject(detail)
                && OptionalTexts(detail, "reason")
                && (
                    !Supplied(detail, "expires_at", out var expires)
                    || (
                        expires.ValueKind == JsonValueKind.String
                        && ReadTime(detail, "expires_at") is not null
                    )
                ),
            "raid" => OptionalSubject(detail) && OptionalSmallNumber(detail, "viewer_count"),
            "delete" or "shared_chat_delete" => OptionalSubject(detail)
                && OptionalTexts(detail, "message_id", "message_body"),
            "add_blocked_term"
            or "add_permitted_term"
            or "remove_blocked_term"
            or "remove_permitted_term" => OptionalTexts(detail, "action", "list")
                && OptionalStrings(detail, "terms")
                && OptionalBoolean(detail, "from_automod"),
            "approve_unban_request" or "deny_unban_request" => OptionalSubject(detail)
                && OptionalTexts(detail, "moderator_message")
                && OptionalBoolean(detail, "is_approved"),
            "warn" => OptionalSubject(detail)
                && OptionalTexts(detail, "reason")
                && OptionalStrings(detail, "chat_rules_cited"),
            _ => true,
        };

    private static bool OptionalSubject(JsonElement detail) =>
        OptionalTexts(detail, "user_id", "user_login", "user_name");

    private static bool Supplied(JsonElement detail, string key, out JsonElement value)
    {
        value = default;
        return detail.ValueKind == JsonValueKind.Object
            && detail.TryGetProperty(key, out value)
            && value.ValueKind != JsonValueKind.Null;
    }

    private static bool OptionalTexts(JsonElement detail, params string[] keys) =>
        keys.All(key =>
            !Supplied(detail, key, out var value) || value.ValueKind == JsonValueKind.String
        );

    private static bool OptionalStrings(JsonElement detail, string key) =>
        !Supplied(detail, key, out var value)
        || (
            value.ValueKind == JsonValueKind.Array
            && value.EnumerateArray().All(item => item.ValueKind == JsonValueKind.String)
        );

    private static bool OptionalBoolean(JsonElement detail, string key) =>
        !Supplied(detail, key, out var value)
        || value.ValueKind is JsonValueKind.True or JsonValueKind.False;

    private static bool OptionalSmallNumber(JsonElement detail, string key) =>
        !Supplied(detail, key, out var value)
        || (
            value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var number)
            && number >= 0
        );

    internal static string ReadText(JsonElement json, string key) =>
        json.ValueKind == JsonValueKind.Object
        && json.TryGetProperty(key, out var v)
        && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? ""
            : "";

    private static int? ReadSmallNumber(JsonElement json, string key) =>
        json.ValueKind != JsonValueKind.Object
        || !json.TryGetProperty(key, out var value)
        || value.ValueKind == JsonValueKind.Null
            ? 0
        : ReadNumber(json, key) is { } amount && amount is >= 0 and <= int.MaxValue ? (int)amount
        : null;

    private static long? ReadNumber(JsonElement json, string key) =>
        json.ValueKind == JsonValueKind.Object && json.TryGetProperty(key, out var v)
            ? v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n)
                ? n
                : v.ValueKind == JsonValueKind.String
                && long.TryParse(
                    v.GetString(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out n
                )
                    ? n
                    : null
            : null;

    private static bool? ReadBool(JsonElement json, string key) =>
        json.ValueKind == JsonValueKind.Object && json.TryGetProperty(key, out var v)
            ? v.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? v.GetBoolean()
                : v.ValueKind == JsonValueKind.String && bool.TryParse(v.GetString(), out var b)
                    ? b
                    : null
            : null;

    private static DateTimeOffset? ReadTime(JsonElement json, string key) =>
        DateTimeOffset.TryParse(
            ReadText(json, key),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out var t
        )
            ? t
            : null;
}
