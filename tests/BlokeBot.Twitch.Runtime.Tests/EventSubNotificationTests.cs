using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Shouldly;

namespace BlokeBot.Twitch.Runtime.Tests;

public sealed class EventSubNotificationTests
{
    private const string _incomingRaidJson = """
        {
          "subscription": {
            "type": "channel.raid",
            "version": "1",
            "condition": { "from_broadcaster_user_id": "", "to_broadcaster_user_id": "target-id" }
          },
          "event": {
            "from_broadcaster_user_id": "source-id",
            "from_broadcaster_user_login": "source_login",
            "from_broadcaster_user_name": "Source Display",
            "to_broadcaster_user_id": "target-id",
            "to_broadcaster_user_login": "target_login",
            "to_broadcaster_user_name": "Target Display",
            "viewers": 42
          }
        }
        """;

    [Test]
    public void ShoutoutReceiveEnvelope_ParsingTypedNotification_MapsProviderCooldowns()
    {
        var notification = Parse(
            """
            {
              "subscription": { "type": "channel.shoutout.receive", "version": "1" },
              "event": {
                "broadcaster_user_id": "host-id", "broadcaster_user_login": "host",
                "from_broadcaster_user_id": "source-id", "from_broadcaster_user_login": "source",
                "to_broadcaster_user_id": "target-id", "to_broadcaster_user_login": "target",
                "viewer_count": 42, "started_at": "2026-07-26T00:00:00Z",
                "cooldown_ends_at": "2026-07-26T01:00:00Z",
                "target_cooldown_ends_at": "2026-07-26T02:00:00Z"
              }
            }
            """,
            "delivery-1"
        );

        var shoutout = notification.ShouldBeOfType<EventSubNotification.Shoutout>().Event;
        shoutout.Direction.ShouldBe(EventSubShoutoutDirection.Received);
        shoutout.MessageId.ShouldBe("delivery-1");
        shoutout.TargetCooldownEndsAt.ShouldBe(
            DateTimeOffset.Parse("2026-07-26T02:00:00Z", CultureInfo.InvariantCulture)
        );
    }

    [Test]
    public void IncomingRaidEnvelope_ParsingTypedNotification_MapsIdentityTimestampAndPayload()
    {
        var notification = Parse(_incomingRaidJson, "raid-message-1");

        var incomingRaid = notification.ShouldBeOfType<EventSubNotification.IncomingRaid>().Event;
        incomingRaid.MessageId.ShouldBe("raid-message-1");
        incomingRaid.MessageTimestamp.ShouldBe(
            DateTimeOffset.Parse("2026-07-29T08:00:00.1234567Z", CultureInfo.InvariantCulture)
        );
        incomingRaid.FromBroadcasterUserId.ShouldBe("source-id");
        incomingRaid.FromBroadcasterUserLogin.ShouldBe("source_login");
        incomingRaid.FromBroadcasterUserName.ShouldBe("Source Display");
        incomingRaid.ToBroadcasterUserId.ShouldBe("target-id");
        incomingRaid.ToBroadcasterUserLogin.ShouldBe("target_login");
        incomingRaid.ToBroadcasterUserName.ShouldBe("Target Display");
        incomingRaid.ViewerCount.ShouldBe(42);
        incomingRaid.SubscriptionDirection.ShouldBe(EventSubRaidSubscriptionDirection.Incoming);
    }

    [Test]
    public void OutgoingRaidEnvelope_MapsSubscriptionConditionDirection()
    {
        var outgoing = JsonNode.Parse(_incomingRaidJson)!.AsObject();
        outgoing["subscription"]!["condition"] = new JsonObject
        {
            ["from_broadcaster_user_id"] = "source-id",
            ["to_broadcaster_user_id"] = "",
        };

        var raid = Parse(outgoing.ToJsonString(), "raid-message-1")
            .ShouldBeOfType<EventSubNotification.IncomingRaid>()
            .Event;

        raid.SubscriptionDirection.ShouldBe(EventSubRaidSubscriptionDirection.Outgoing);
    }

    [Test]
    public void IncomingRaidEnvelope_UnsupportedVersion_IsRejected()
    {
        var wrongVersion = JsonNode.Parse(_incomingRaidJson)!.AsObject();
        wrongVersion["subscription"]!["version"] = "2";

        _ = Parse(wrongVersion.ToJsonString(), "raid-message-1")
            .ShouldBeOfType<EventSubNotification.Unknown>();
    }

    internal static EventSubEnvelope IncomingRaidEnvelope() =>
        Deserialize(_incomingRaidJson, "raid-message-1");

    private static EventSubNotification Parse(string json, string messageId) =>
        EventSubNotification.Parse(
            Deserialize(json, messageId),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
        );

    private static EventSubEnvelope Deserialize(string json, string messageId)
    {
        var envelope = JsonSerializer.Deserialize<EventSubEnvelope>(json)!;
        var subscription = envelope.Subscription!.Value;
        envelope.Metadata = new EventSubMetadata
        {
            MessageId = messageId,
            MessageType = "notification",
            SubscriptionType = subscription.GetProperty("type").GetString()!,
            SubscriptionVersion = subscription.GetProperty("version").GetString()!,
            MessageTimestamp =
                messageId == "raid-message-1"
                    ? DateTimeOffset.Parse(
                        "2026-07-29T08:00:00.1234567Z",
                        CultureInfo.InvariantCulture
                    )
                    : null,
        };
        return envelope;
    }
}
