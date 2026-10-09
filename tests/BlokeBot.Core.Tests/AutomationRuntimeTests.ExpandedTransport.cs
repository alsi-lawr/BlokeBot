using System.Text.Json;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace BlokeBot.Core.Tests;

public sealed partial class AutomationRuntimeTests
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
    public async Task ModerateV2_DocumentedTransportToRealFlow_RetainsSharedSourceAndFencesRepeatedDelivery(
        string action
    )
    {
        await using var f = await RuntimeFixture.CreateAsync();
        _ = await SaveExpandedChatAsync(
            f,
            "moderation-action",
            $"{{\"action\":\"{action}\"}}",
            "moderated"
        );
        var x = ExpandedFixtureFor(f);
        var handler = ExpandedHandler(x.Runtime);
        var detail =
            action.Contains("_term", StringComparison.Ordinal) ? "automod_terms"
            : action.EndsWith("unban_request", StringComparison.Ordinal) ? "unban_request"
            : action;
        var payload = new Dictionary<string, object?>
        {
            ["broadcaster_user_id"] = "streamer-id",
            ["source_broadcaster_user_id"] = "remote-id",
            ["source_broadcaster_user_login"] = "remote",
            ["moderator_user_id"] = "moderator-id",
            ["moderator_user_login"] = "moderator",
            ["moderator_user_name"] = "Moderator",
            ["action"] = action,
            [detail] = new
            {
                user_id = "subject-id",
                user_login = "subject",
                user_name = "Subject",
                reason = "private reason",
                message_body = "private body",
                terms = new[] { "private term" },
                expires_at = f.Clock.GetUtcNow().AddSeconds(30),
                viewer_count = 4,
            },
        };
        var envelope = ExpandedEnvelope(f, "channel.moderate", "2", payload, "provider-occurrence");
        await handler.DispatchNotificationAsync(envelope, "{}", CancellationToken.None);
        await handler.DispatchNotificationAsync(envelope, "{}", CancellationToken.None);
        f.Chat.Messages.ShouldBe(["moderated"]);
        await using var db = await f.Database.CreateDbContextAsync();
        var context = (await db.AutomationFlowRuns.SingleAsync()).ContextJson;
        context.ShouldContain("remote-id");
        context.ShouldNotContain("private reason");
        context.ShouldNotContain("private body");
        context.ShouldNotContain("private term");
        payload["broadcaster_user_id"] = "other-host-id";
        await handler.DispatchNotificationAsync(
            ExpandedEnvelope(f, "channel.moderate", "2", payload, "other-occurrence"),
            "{}",
            CancellationToken.None
        );
        f.Chat.Messages.Count.ShouldBe(1);
    }

    [Test]
    public async Task OverlappingObservations_HaveOneCanonicalRoute_PerSourceNode_WithoutCrossTypeCollapse()
    {
        await using var f = await RuntimeFixture.CreateAsync(
            hostFeatures: HostFeatureFlags.Automations | HostFeatureFlags.RaidCollaboration
        );
        _ = await SaveExpandedChatAsync(
            f,
            "moderation-action",
            "{\"action\":\"any\"}",
            "moderation"
        );
        _ = await SaveExpandedChatAsync(f, "chat-settings-changed", "{}", "settings");
        _ = await SaveExpandedChatAsync(f, "outgoing-raid", "{}", "outgoing");
        var handler = ExpandedHandler(ExpandedFixtureFor(f).Runtime);
        var mod = ExpandedEnvelope(
            f,
            "channel.moderate",
            "2",
            new
            {
                broadcaster_user_id = "streamer-id",
                source_broadcaster_user_id = "streamer-id",
                moderator_user_id = "mod",
                action = "raid",
                raid = new
                {
                    user_id = "target-id",
                    user_login = "target",
                    user_name = "Target",
                    viewer_count = 10,
                },
            },
            "moderation-raid"
        );
        await handler.DispatchNotificationAsync(mod, "{}", CancellationToken.None);
        await handler.DispatchNotificationAsync(mod, "{}", CancellationToken.None);
        f.Chat.Messages.ShouldBe(["moderation"]);
        var settings = ExpandedEnvelope(
            f,
            "channel.chat_settings.update",
            "1",
            new
            {
                broadcaster_user_id = "streamer-id",
                slow_mode = true,
                slow_mode_wait_time_seconds = 30,
                subscriber_mode = false,
                emote_mode = false,
                follower_mode = false,
                follower_mode_duration_minutes = 0,
                unique_chat_mode = false,
            },
            "settings-state"
        );
        await handler.DispatchNotificationAsync(settings, "{}", CancellationToken.None);
        await handler.DispatchNotificationAsync(settings, "{}", CancellationToken.None);
        var raidData = new
        {
            from_broadcaster_user_id = "streamer-id",
            from_broadcaster_user_login = "streamer",
            from_broadcaster_user_name = "Streamer",
            to_broadcaster_user_id = "target-id",
            to_broadcaster_user_login = "target",
            to_broadcaster_user_name = "Target",
            viewers = 10,
        };
        var outgoing = ExpandedEnvelope(
            f,
            "channel.raid",
            "1",
            raidData,
            "raid-result",
            new { from_broadcaster_user_id = "streamer-id", to_broadcaster_user_id = "" }
        );
        await handler.DispatchNotificationAsync(outgoing, "{}", CancellationToken.None);
        await handler.DispatchNotificationAsync(outgoing, "{}", CancellationToken.None);
        var incoming = ExpandedEnvelope(
            f,
            "channel.raid",
            "1",
            raidData,
            "raid-result",
            new { from_broadcaster_user_id = "", to_broadcaster_user_id = "target-id" }
        );
        await handler.DispatchNotificationAsync(incoming, "{}", CancellationToken.None);
        f.Chat.Messages.ShouldBe(["moderation", "settings", "outgoing"]);
    }

    private static EventSubDeliveryHandler ExpandedHandler(ExpandedAutomationRuntime runtime) =>
        new(
            null!,
            null!,
            new EnabledNativeTwitchFeatureStateProvider(),
            [],
            null!,
            expandedObservers: [runtime]
        );

    private static EventSubEnvelope ExpandedEnvelope(
        RuntimeFixture f,
        string type,
        string version,
        object data,
        string id,
        object? condition = null
    ) =>
        new()
        {
            Subscription = JsonSerializer.SerializeToElement(
                new
                {
                    type,
                    version,
                    condition,
                }
            ),
            Event = JsonSerializer.SerializeToElement(data),
            Metadata = new()
            {
                MessageId = id,
                MessageTimestamp = f.Clock.GetUtcNow(),
                SubscriptionType = type,
                SubscriptionVersion = version,
            },
        };
}
