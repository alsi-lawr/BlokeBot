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
                )
                {
                    return new Unknown();
                }
                var key =
                    action is "approve_unban_request" or "deny_unban_request" ? "unban_request"
                    : action.Contains("_term", StringComparison.Ordinal) ? "automod_terms"
                    : action;
                var detail =
                    payload.TryGetProperty(key, out var value)
                    && value.ValueKind == JsonValueKind.Object
                        ? value
                        : default;
                if (
                    (
                        payload.TryGetProperty(key, out var declared)
                        && declared.ValueKind is not (JsonValueKind.Object or JsonValueKind.Null)
                    )
                    || ReadSmallNumber(detail, "wait_time_seconds") is not { } wait
                    || ReadSmallNumber(detail, "viewer_count") is not { } viewers
                )
                {
                    return new Unknown();
                }
                var moderationDuration = ReadTime(detail, "expires_at") is { } expires
                    ? Math.Clamp((expires - at).TotalSeconds, 0, int.MaxValue)
                    : wait;
                var count =
                    detail.ValueKind == JsonValueKind.Object
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
