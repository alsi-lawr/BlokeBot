using System.Text.Json;

namespace BlokeBot.Twitch.Runtime;

internal readonly record struct EventSubSignedSubscriptionIdentity(
    string Id,
    string Type,
    string Version
)
{
    internal static bool TryParse(
        ReadOnlySpan<byte> signedBody,
        string? headerType,
        string? headerVersion,
        out EventSubEnvelope envelope,
        out EventSubSignedSubscriptionIdentity identity
    )
    {
        envelope = null!;
        identity = default;
        EventSubEnvelope? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<EventSubEnvelope>(signedBody);
        }
        catch (JsonException)
        {
            return false;
        }

        if (
            parsed?.Subscription is not { ValueKind: JsonValueKind.Object } subscription
            || !TryReadRequiredString(subscription, "type", out var type)
            || !TryReadRequiredString(subscription, "version", out var version)
            || !string.Equals(headerType, type, StringComparison.Ordinal)
            || !string.Equals(headerVersion, version, StringComparison.Ordinal)
        )
        {
            return false;
        }

        var id =
            subscription.TryGetProperty("id", out var idElement)
            && idElement.ValueKind is JsonValueKind.String
                ? idElement.GetString() ?? string.Empty
                : string.Empty;
        envelope = parsed;
        identity = new(id, type, version);
        return true;
    }

    private static bool TryReadRequiredString(
        JsonElement subscription,
        string propertyName,
        out string value
    )
    {
        value = string.Empty;
        if (
            !subscription.TryGetProperty(propertyName, out var element)
            || element.ValueKind is not JsonValueKind.String
            || string.IsNullOrWhiteSpace(element.GetString())
        )
        {
            return false;
        }

        value = element.GetString()!;
        return true;
    }
}
