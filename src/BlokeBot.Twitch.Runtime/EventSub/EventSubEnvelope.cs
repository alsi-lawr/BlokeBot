using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlokeBot.Twitch.Runtime;

[JsonConverter(typeof(EventSubEnvelopeJsonConverter))]
internal sealed record EventSubEnvelope
{
    [JsonPropertyName("metadata")]
    public EventSubMetadata Metadata { get; init; } = new();

    [JsonPropertyName("payload")]
    public EventSubPayload Payload { get; init; } = new();
}

internal sealed class EventSubEnvelopeJsonConverter : JsonConverter<EventSubEnvelope>
{
    public override EventSubEnvelope? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        if (!IsIncomingRaid(root))
        {
            return root.Deserialize<EventSubEnvelopeWire>(options)?.ToDomain();
        }

        var metadata = root.GetProperty("metadata");
        return new()
        {
            Metadata = new()
            {
                MessageId = ReadString(metadata, "message_id"),
                MessageType = ReadString(metadata, "message_type"),
                SubscriptionType = ReadString(metadata, "subscription_type"),
                SubscriptionVersion = ReadString(metadata, "subscription_version"),
                MessageTimestamp = ReadTimestamp(metadata),
            },
            Payload = ReadIncomingRaidPayload(root),
        };
    }

    public override void Write(
        Utf8JsonWriter writer,
        EventSubEnvelope value,
        JsonSerializerOptions options
    )
    {
        throw new NotSupportedException("EventSub envelopes are receive-only.");
    }

    private static bool IsIncomingRaid(JsonElement root)
    {
        return root.ValueKind is JsonValueKind.Object
            && root.TryGetProperty("metadata", out var metadata)
            && metadata.ValueKind is JsonValueKind.Object
            && metadata.TryGetProperty("subscription_type", out var subscriptionType)
            && subscriptionType.ValueKind is JsonValueKind.String
            && subscriptionType.GetString() == "channel.raid";
    }

    private static string ReadString(JsonElement metadata, string propertyName)
    {
        return
            metadata.TryGetProperty(propertyName, out var value)
            && value.ValueKind is JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    private static DateTimeOffset? ReadTimestamp(JsonElement metadata)
    {
        return
            metadata.TryGetProperty("message_timestamp", out var value)
            && value.ValueKind is JsonValueKind.String
            && value.TryGetDateTimeOffset(out var timestamp)
            ? timestamp
            : null;
    }

    private static EventSubPayload ReadIncomingRaidPayload(JsonElement root)
    {
        if (
            !root.TryGetProperty("payload", out var payload)
            || payload.ValueKind is not JsonValueKind.Object
            || !payload.TryGetProperty("event", out var eventPayload)
        )
        {
            return new();
        }

        return new() { Event = eventPayload.Clone() };
    }

    private sealed record EventSubEnvelopeWire
    {
        [JsonPropertyName("metadata")]
        public EventSubMetadata Metadata { get; init; } = new();

        [JsonPropertyName("payload")]
        public EventSubPayload Payload { get; init; } = new();

        internal EventSubEnvelope ToDomain()
        {
            return new() { Metadata = Metadata, Payload = Payload };
        }
    }
}
