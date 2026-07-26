using System.Text.Json;
using BlokeBot.Twitch.Runtime;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Twitch.Runtime.Tests;

public sealed class EventSubNotificationTests
{
    [Test]
    public void ShoutoutReceiveEnvelope_ParsingTypedNotification_MapsProviderCooldowns()
    {
        var envelope = JsonSerializer.Deserialize<EventSubEnvelope>(
            """
            {
              "metadata": { "message_id": "delivery-1", "subscription_type": "channel.shoutout.receive" },
              "payload": { "event": {
                "broadcaster_user_id": "host-id", "broadcaster_user_login": "host",
                "from_broadcaster_user_id": "source-id", "from_broadcaster_user_login": "source",
                "to_broadcaster_user_id": "target-id", "to_broadcaster_user_login": "target",
                "viewer_count": 42, "started_at": "2026-07-26T00:00:00Z",
                "cooldown_ends_at": "2026-07-26T01:00:00Z",
                "target_cooldown_ends_at": "2026-07-26T02:00:00Z"
              } }
            }
            """
        )!;

        var notification = EventSubNotification.Parse(
            envelope,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
        );

        var shoutout = notification.ShouldBeOfType<EventSubNotification.Shoutout>().Event;
        shoutout.Direction.ShouldBe(EventSubShoutoutDirection.Received);
        shoutout.MessageId.ShouldBe("delivery-1");
        shoutout.TargetCooldownEndsAt.ShouldBe(DateTimeOffset.Parse("2026-07-26T02:00:00Z"));
    }
}
