using System.Collections.Immutable;
using System.Text.Json;
using BlokeBot.Plugins.Contracts;

namespace BlokeBot.Core.Features.Plugins;

internal static class PluginRawEventSubEnvelopeMapper
{
    internal static bool TryMap(EventSubRawNotification notification, out PluginValue.Map envelope)
    {
        try
        {
            using var document = JsonDocument.Parse(notification.EventJson);
            if (
                document.RootElement.ValueKind is not JsonValueKind.Object
                || Map(document.RootElement) is not PluginValue.Map eventPayload
            )
            {
                envelope = null!;
                return false;
            }

            envelope = PluginInvocationInputs.TwitchRawEvent(
                notification.SubscriptionType,
                notification.SubscriptionVersion,
                eventPayload
            );
            return PluginValueValidator.Validate(envelope) is PluginValueValidationOutcome.Valid;
        }
        catch (JsonException)
        {
            envelope = null!;
            return false;
        }
    }

    private static PluginValue? Map(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.Null => new PluginValue.Nil(),
            JsonValueKind.True => new PluginValue.Boolean(true),
            JsonValueKind.False => new PluginValue.Boolean(false),
            JsonValueKind.Number when element.TryGetDouble(out var value) => new PluginValue.Number(
                value
            ),
            JsonValueKind.String => new PluginValue.String(element.GetString() ?? string.Empty),
            JsonValueKind.Array => MapArray(element),
            JsonValueKind.Object => MapObject(element),
            _ => null,
        };

    private static PluginValue.Array? MapArray(JsonElement element)
    {
        var items = ImmutableArray.CreateBuilder<PluginValue>();
        foreach (var item in element.EnumerateArray())
        {
            if (Map(item) is not { } mapped)
            {
                return null;
            }
            items.Add(mapped);
        }
        return new(items.ToImmutable());
    }

    private static PluginValue.Map? MapObject(JsonElement element)
    {
        var properties = ImmutableArray.CreateBuilder<PluginValueProperty>();
        foreach (var property in element.EnumerateObject())
        {
            if (Map(property.Value) is not { } mapped)
            {
                return null;
            }
            properties.Add(new(property.Name, mapped));
        }
        return new(properties.ToImmutable());
    }
}
