using System.Text;
using System.Text.Json;
using BlokeBot.Plugins.Contracts;

namespace BlokeBot.Plugins.Features;

public sealed class PluginSettingValuesCodec
{
    public PluginSettingValuesEncodingOutcome Encode(PluginSettingValues values)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            foreach (var entry in values.Entries.OrderBy(static entry => entry.SettingId.Value))
            {
                writer.WriteStartObject();
                writer.WriteString("id", entry.SettingId.Value);
                _ = entry.Value.Match(
                    boolean => Write(writer, "boolean", boolean.Value),
                    text => Write(writer, "text", text.Value),
                    integer => Write(writer, "integer", integer.Value),
                    number => Write(writer, "number", number.Value),
                    duration => Write(writer, "duration", duration.Seconds),
                    choice => Write(writer, "choice", choice.Value.Value)
                );
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }

        return stream.Length <= PluginContractLimits.MaximumOrdinarySettingsJsonBytes
            ? new PluginSettingValuesEncodingOutcome.Encoded(
                Encoding.UTF8.GetString(stream.GetBuffer(), 0, checked((int)stream.Length))
            )
            : new PluginSettingValuesEncodingOutcome.TooLarge();
    }

    public PluginSettingValuesDecodingOutcome Decode(string json)
    {
        if (
            Encoding.UTF8.GetByteCount(json) > PluginContractLimits.MaximumOrdinarySettingsJsonBytes
        )
        {
            return new PluginSettingValuesDecodingOutcome.Invalid();
        }

        try
        {
            using var document = JsonDocument.Parse(
                json,
                new() { AllowTrailingCommas = false, MaxDepth = 4 }
            );
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return new PluginSettingValuesDecodingOutcome.Invalid();
            }

            var entries = new List<PluginSettingValueEntry>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (!TryRead(item, out var entry))
                {
                    return new PluginSettingValuesDecodingOutcome.Invalid();
                }
                entries.Add(entry);
            }

            return PluginSettingValues.Create(entries) switch
            {
                PluginSettingValuesOutcome.Created created =>
                    new PluginSettingValuesDecodingOutcome.Decoded(created.Values),
                PluginSettingValuesOutcome.DuplicateSetting =>
                    new PluginSettingValuesDecodingOutcome.Invalid(),
                _ => new PluginSettingValuesDecodingOutcome.Invalid(),
            };
        }
        catch (JsonException)
        {
            return new PluginSettingValuesDecodingOutcome.Invalid();
        }
    }

    private static bool TryRead(JsonElement item, out PluginSettingValueEntry entry)
    {
        entry = null!;
        if (
            item.ValueKind != JsonValueKind.Object
            || item.EnumerateObject().Count() != 3
            || !item.TryGetProperty("id", out var idElement)
            || !item.TryGetProperty("kind", out var kindElement)
            || !item.TryGetProperty("value", out var valueElement)
            || idElement.ValueKind != JsonValueKind.String
            || kindElement.ValueKind != JsonValueKind.String
            || !PluginSettingId.TryCreate(idElement.GetString(), out var settingId)
            || kindElement.GetString() is not { } kind
            || !TryValue(kind, valueElement, out var value)
        )
        {
            return false;
        }

        entry = new(settingId, value);
        return true;
    }

    private static bool TryValue(string kind, JsonElement element, out PluginSettingValue value)
    {
        value = null!;
        switch (kind)
        {
            case "boolean" when element.ValueKind is JsonValueKind.True or JsonValueKind.False:
                value = new PluginSettingValue.Boolean(element.GetBoolean());
                return true;
            case "text" when element.ValueKind == JsonValueKind.String:
                value = new PluginSettingValue.Text(element.GetString()!);
                return true;
            case "integer" when element.TryGetInt64(out var integer):
                value = new PluginSettingValue.Integer(integer);
                return true;
            case "number" when element.TryGetDecimal(out var number):
                value = new PluginSettingValue.Number(number);
                return true;
            case "duration" when element.TryGetInt64(out var seconds):
                value = new PluginSettingValue.Duration(seconds);
                return true;
            case "choice"
                when element.ValueKind == JsonValueKind.String
                    && PluginSettingChoiceId.TryCreate(element.GetString(), out var choice):
                value = new PluginSettingValue.Choice(choice);
                return true;
            default:
                return false;
        }
    }

    private static bool Write(Utf8JsonWriter writer, string kind, bool value)
    {
        writer.WriteString("kind", kind);
        writer.WriteBoolean("value", value);
        return true;
    }

    private static bool Write(Utf8JsonWriter writer, string kind, long value)
    {
        writer.WriteString("kind", kind);
        writer.WriteNumber("value", value);
        return true;
    }

    private static bool Write(Utf8JsonWriter writer, string kind, decimal value)
    {
        writer.WriteString("kind", kind);
        writer.WriteNumber("value", value);
        return true;
    }

    private static bool Write(Utf8JsonWriter writer, string kind, string value)
    {
        writer.WriteString("kind", kind);
        writer.WriteString("value", value);
        return true;
    }
}

public abstract record PluginSettingValuesEncodingOutcome
{
    private PluginSettingValuesEncodingOutcome() { }

    public sealed record Encoded(string Json) : PluginSettingValuesEncodingOutcome;

    public sealed record TooLarge : PluginSettingValuesEncodingOutcome;
}

public abstract record PluginSettingValuesDecodingOutcome
{
    private PluginSettingValuesDecodingOutcome() { }

    public sealed record Decoded(PluginSettingValues Values) : PluginSettingValuesDecodingOutcome;

    public sealed record Invalid : PluginSettingValuesDecodingOutcome;
}
