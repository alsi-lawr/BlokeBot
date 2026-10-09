using System.Text.Json;
using Shouldly;

namespace BlokeBot.Twitch.Runtime.Tests;

public sealed class ExpandedEventSubNotificationTests
{
    [Test]
    [Arguments("shared_chat_ban")]
    [Arguments("shared_chat_timeout")]
    [Arguments("shared_chat_unban")]
    [Arguments("shared_chat_untimeout")]
    [Arguments("shared_chat_delete")]
    [Arguments("add_blocked_term")]
    [Arguments("add_permitted_term")]
    [Arguments("remove_blocked_term")]
    [Arguments("remove_permitted_term")]
    [Arguments("approve_unban_request")]
    [Arguments("deny_unban_request")]
    [Arguments("warn")]
    [Arguments("ban")]
    [Arguments("timeout")]
    [Arguments("unban")]
    [Arguments("untimeout")]
    [Arguments("clear")]
    [Arguments("emoteonly")]
    [Arguments("emoteonlyoff")]
    [Arguments("followers")]
    [Arguments("followersoff")]
    [Arguments("uniquechat")]
    [Arguments("uniquechatoff")]
    [Arguments("slow")]
    [Arguments("slowoff")]
    [Arguments("subscribers")]
    [Arguments("subscribersoff")]
    [Arguments("unraid")]
    [Arguments("delete")]
    [Arguments("unvip")]
    [Arguments("vip")]
    [Arguments("raid")]
    [Arguments("mod")]
    [Arguments("unmod")]
    public void ModerateV2_ProjectsSupportedActionAndActualSource_WithoutPrivatePayload(
        string action
    )
    {
        var detailKey =
            action.Contains("_term", StringComparison.Ordinal) ? "automod_terms"
            : action.EndsWith("unban_request", StringComparison.Ordinal) ? "unban_request"
            : action;
        var data = new Dictionary<string, object?>
        {
            { "broadcaster_user_id", "host" },
            { "source_broadcaster_user_id", "actual-source" },
            { "source_broadcaster_user_login", "source" },
            { "moderator_user_id", "mod" },
            { "moderator_user_login", "moderator" },
            { "moderator_user_name", "Moderator" },
            { "action", action },
            {
                detailKey,
                new
                {
                    user_login = "subject",
                    user_name = "Subject",
                    reason = "private reason",
                    message_body = "private body",
                    terms = new[] { "private term" },
                    chat_rules_cited = new[] { "private rule" },
                    moderator_message = "private moderator message",
                    expires_at = "2026-10-03T12:01:00Z",
                }
            },
        };
        var notification = Parse("channel.moderate", "2", JsonSerializer.Serialize(data));
        var projected = notification.ShouldBeOfType<EventSubNotification.Moderation>().Event;
        projected.Action.ShouldBe(action);
        projected.SourceBroadcasterId.ShouldBe("actual-source");
        var serialized = JsonSerializer.Serialize(projected);
        serialized.ShouldNotContain("private");
        _ = Parse("channel.moderate", "1", JsonSerializer.Serialize(data))
            .ShouldBeOfType<EventSubNotification.Unknown>();
    }

    [Test]
    public void TypedAdGoalSettings_UseActualVersions_RejectMissingAuthorityAndMalformedAmounts()
    {
        var ad = Parse(
                "channel.ad_break.begin",
                "1",
                """{"broadcaster_user_id":"host","duration_seconds":"30","started_at":"2026-10-03T12:00:00Z","is_automatic":"false"}"""
            )
            .ShouldBeOfType<EventSubNotification.AdBreak>()
            .Event;
        ad.DurationSeconds.ShouldBe(30);
        ad.IsAutomatic.ShouldBeFalse();
        _ = Parse(
                "channel.ad_break.begin",
                "1",
                """{"broadcaster_user_id":"host","duration_seconds":-2,"started_at":"2026-10-03T12:00:00Z","is_automatic":true}"""
            )
            .ShouldBeOfType<EventSubNotification.Unknown>();
        var goal = Parse(
                "channel.goal.progress",
                "1",
                """{"broadcaster_user_id":"host","id":"goal","type":"follower","current_amount":12,"target_amount":20}"""
            )
            .ShouldBeOfType<EventSubNotification.Goal>()
            .Event;
        goal.CurrentAmount.ShouldBe(12);
        goal.Stage.ShouldBe(EventSubGoalStage.Progress);
        _ = Parse(
                "channel.goal.progress",
                "1",
                """{"broadcaster_user_id":"host","id":"goal","current_amount":-1,"target_amount":20}"""
            )
            .ShouldBeOfType<EventSubNotification.Unknown>();
        var settings = Parse(
                "channel.chat_settings.update",
                "1",
                """{"broadcaster_user_id":"host","slow_mode":true,"slow_mode_wait_time_seconds":30,"subscriber_mode":false,"emote_mode":false,"follower_mode":true,"follower_mode_duration_minutes":10,"unique_chat_mode":false}"""
            )
            .ShouldBeOfType<EventSubNotification.ChatSettings>()
            .Event;
        settings.SlowSeconds.ShouldBe(30);
        settings.FollowerMinutes.ShouldBe(10);
        _ = Parse("channel.chat_settings.update", "2", "{}")
            .ShouldBeOfType<EventSubNotification.Unknown>();
    }

    [Test]
    [Arguments(
        "{\"broadcaster_user_id\":\"host\",\"source_broadcaster_user_id\":\"source\",\"moderator_user_id\":\"mod\",\"action\":\"not-an-action\"}"
    )]
    [Arguments(
        "{\"broadcaster_user_id\":\"host\",\"source_broadcaster_user_id\":\"source\",\"action\":\"ban\"}"
    )]
    [Arguments(
        "{\"broadcaster_user_id\":\"host\",\"source_broadcaster_user_id\":\"source\",\"moderator_user_id\":\"mod\",\"action\":\"ban\",\"ban\":[]}"
    )]
    [Arguments(
        "{\"broadcaster_user_id\":\"host\",\"source_broadcaster_user_id\":\"source\",\"moderator_user_id\":\"mod\",\"action\":\"slow\",\"slow\":{\"wait_time_seconds\":-1}}"
    )]
    [Arguments(
        "{\"broadcaster_user_id\":\"host\",\"source_broadcaster_user_id\":\"source\",\"moderator_user_id\":\"mod\",\"action\":\"raid\",\"raid\":{\"viewer_count\":9223372036854775807}}"
    )]
    public void ModerateV2_UnknownOrMalformedObservation_DoesNotFabricateTypedSuccess(
        string payload
    ) => _ = Parse("channel.moderate", "2", payload).ShouldBeOfType<EventSubNotification.Unknown>();

    private static EventSubNotification Parse(string type, string version, string data)
    {
        var envelope = JsonSerializer.Deserialize<EventSubEnvelope>($"{{\"event\":{data}}}")!;
        envelope.Metadata = new()
        {
            MessageId = "delivery",
            MessageType = "notification",
            SubscriptionType = type,
            SubscriptionVersion = version,
            MessageTimestamp = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero),
        };
        return EventSubNotification.Parse(envelope, new(JsonSerializerDefaults.Web));
    }
}
