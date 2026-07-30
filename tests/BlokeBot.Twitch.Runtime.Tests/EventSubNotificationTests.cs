using System.Text.Json;
using System.Text.Json.Nodes;
using BlokeBot.Twitch.Runtime;
using Shouldly;
using TUnit.Core;

namespace BlokeBot.Twitch.Runtime.Tests;

public sealed class EventSubNotificationTests
{
    private const string _incomingRaidJson = """
        {
          "metadata": {
            "message_id": "raid-message-1",
            "message_timestamp": "2026-07-29T08:00:00.1234567Z",
            "subscription_type": "channel.raid",
            "subscription_version": "1"
          },
          "payload": { "event": {
            "from_broadcaster_user_id": "source-id",
            "from_broadcaster_user_login": "source_login",
            "from_broadcaster_user_name": "Source Display",
            "to_broadcaster_user_id": "target-id",
            "to_broadcaster_user_login": "target_login",
            "to_broadcaster_user_name": "Target Display",
            "viewers": 42
          } }
        }
        """;

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

    [Test]
    public void IncomingRaidEnvelope_ParsingTypedNotification_MapsIdentityTimestampAndPayload()
    {
        var notification = Parse(_incomingRaidJson);

        var incomingRaid = notification.ShouldBeOfType<EventSubNotification.IncomingRaid>().Event;
        incomingRaid.MessageId.ShouldBe("raid-message-1");
        incomingRaid.MessageTimestamp.ShouldBe(
            DateTimeOffset.Parse("2026-07-29T08:00:00.1234567Z")
        );
        incomingRaid.FromBroadcasterUserId.ShouldBe("source-id");
        incomingRaid.FromBroadcasterUserLogin.ShouldBe("source_login");
        incomingRaid.FromBroadcasterUserName.ShouldBe("Source Display");
        incomingRaid.ToBroadcasterUserId.ShouldBe("target-id");
        incomingRaid.ToBroadcasterUserLogin.ShouldBe("target_login");
        incomingRaid.ToBroadcasterUserName.ShouldBe("Target Display");
        incomingRaid.ViewerCount.ShouldBe(42);
    }

    [Test]
    public void IncomingRaidEnvelope_UnsupportedVersion_IsRejected()
    {
        var wrongVersion = JsonNode.Parse(_incomingRaidJson)!.AsObject();
        wrongVersion["metadata"]!["subscription_version"] = "2";

        Parse(wrongVersion.ToJsonString()).ShouldBeOfType<EventSubNotification.Unknown>();
    }

    internal static EventSubEnvelope IncomingRaidEnvelope()
    {
        return JsonSerializer.Deserialize<EventSubEnvelope>(_incomingRaidJson)!;
    }

    private static EventSubNotification Parse(string json)
    {
        return EventSubNotification.Parse(
            JsonSerializer.Deserialize<EventSubEnvelope>(json)!,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
        );
    }
}
