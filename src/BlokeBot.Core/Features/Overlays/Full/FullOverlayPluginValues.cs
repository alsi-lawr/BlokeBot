using System.Text.Json;
using BlokeBot.Plugins.Contracts;

namespace BlokeBot.Core.Features.Overlays.Full;

internal static class FullOverlayPluginValues
{
    internal static PluginValue FromJson(JsonElement value) =>
        value.ValueKind switch
        {
            JsonValueKind.Null => new PluginValue.Nil(),
            JsonValueKind.True => new PluginValue.Boolean(true),
            JsonValueKind.False => new PluginValue.Boolean(false),
            JsonValueKind.String => new PluginValue.String(value.GetString()!),
            JsonValueKind.Number => new PluginValue.Number(
                value.TryGetDouble(out var number) ? number : double.NaN
            ),
            JsonValueKind.Array => new PluginValue.Array([
                .. value.EnumerateArray().Select(FromJson),
            ]),
            JsonValueKind.Object => new PluginValue.Map([
                .. value
                    .EnumerateObject()
                    .Select(property => new PluginValueProperty(
                        property.Name,
                        FromJson(property.Value)
                    )),
            ]),
            JsonValueKind.Undefined => new PluginValue.Nil(),
        };

    internal static JsonElement ToJson(PluginValue value)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            Write(writer, value);
        }
        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    private static void Write(Utf8JsonWriter writer, PluginValue value)
    {
        switch (value)
        {
            case PluginValue.Nil:
                writer.WriteNullValue();
                break;
            case PluginValue.Boolean boolean:
                writer.WriteBooleanValue(boolean.Value);
                break;
            case PluginValue.Number number:
                writer.WriteNumberValue(number.Value);
                break;
            case PluginValue.String text:
                writer.WriteStringValue(text.Value);
                break;
            case PluginValue.Array array:
                writer.WriteStartArray();
                foreach (var item in array.Items)
                {
                    Write(writer, item);
                }
                writer.WriteEndArray();
                break;
            case PluginValue.Map map:
                writer.WriteStartObject();
                foreach (var property in map.Properties)
                {
                    writer.WritePropertyName(property.Name);
                    Write(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
        }
    }
}
