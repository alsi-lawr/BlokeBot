using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using BlokeBot.Plugins.Contracts;

namespace BlokeBot.Core.Features.Automations;

internal static class AutomationStructuredValue
{
    internal static string Serialize(AutomationValue value)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            Write(writer, value);
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    internal static void Write(Utf8JsonWriter writer, AutomationValue value)
    {
        switch (value)
        {
            case AutomationValue.Nil:
                writer.WriteNullValue();
                break;
            case AutomationValue.Text text:
                writer.WriteStringValue(text.Value);
                break;
            case AutomationValue.Number number:
                writer.WriteNumberValue(number.Value);
                break;
            case AutomationValue.Boolean boolean:
                writer.WriteBooleanValue(boolean.Value);
                break;
            case AutomationValue.Array array:
                writer.WriteStartArray();
                foreach (var item in array.Items)
                {
                    Write(writer, item);
                }
                writer.WriteEndArray();
                break;
            case AutomationValue.Map map:
                writer.WriteStartObject();
                foreach (var property in map.Properties)
                {
                    writer.WritePropertyName(property.Name);
                    Write(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            default:
                throw new InvalidOperationException("The value is not JSON-like automation data.");
        }
    }

    internal static bool TryRead(JsonElement json, out AutomationValue value)
    {
        if (!TryPluginValue(json, out var pluginValue))
        {
            value = null!;
            return false;
        }

        if (PluginValueValidator.Validate(pluginValue) is PluginValueValidationOutcome.Invalid)
        {
            value = null!;
            return false;
        }

        return TryConvert(pluginValue, out value);
    }

    internal static bool TryConvert(PluginValue value, out AutomationValue converted)
    {
        converted = value switch
        {
            PluginValue.Nil => new AutomationValue.Nil(),
            PluginValue.Boolean boolean => new AutomationValue.Boolean(boolean.Value),
            PluginValue.Number number when TryDecimal(number.Value, out var decimalValue) =>
                new AutomationValue.Number(decimalValue),
            PluginValue.String text => new AutomationValue.Text(text.Value),
            PluginValue.Array array when TryArray(array, out var automationArray) =>
                automationArray,
            PluginValue.Map map when TryMap(map, out var automationMap) => automationMap,
            _ => null!,
        };
        return converted is not null;
    }

    internal static PluginValue ToPluginValue(AutomationValue value) =>
        value switch
        {
            AutomationValue.Nil => new PluginValue.Nil(),
            AutomationValue.Text text => new PluginValue.String(text.Value),
            AutomationValue.Number number => new PluginValue.Number((double)number.Value),
            AutomationValue.Boolean boolean => new PluginValue.Boolean(boolean.Value),
            AutomationValue.Array array => new PluginValue.Array(
                array.Items.Select(ToPluginValue).ToImmutableArray()
            ),
            AutomationValue.Map map => new PluginValue.Map(
                map.Properties.Select(property => new PluginValueProperty(
                        property.Name,
                        ToPluginValue(property.Value)
                    ))
                    .ToImmutableArray()
            ),
            AutomationValue.Null => new PluginValue.Nil(),
            _ => throw new InvalidOperationException(
                "The automation value cannot cross the plugin structured-value boundary."
            ),
        };

    private static bool TryPluginValue(JsonElement json, out PluginValue value)
    {
        value = json.ValueKind switch
        {
            JsonValueKind.Null => new PluginValue.Nil(),
            JsonValueKind.True => new PluginValue.Boolean(true),
            JsonValueKind.False => new PluginValue.Boolean(false),
            JsonValueKind.Number when json.TryGetDouble(out var number) => new PluginValue.Number(
                number
            ),
            JsonValueKind.String => new PluginValue.String(json.GetString()!),
            JsonValueKind.Array when TryPluginArray(json, out var array) => array,
            JsonValueKind.Object when TryPluginMap(json, out var map) => map,
            _ => null!,
        };
        return value is not null;
    }

    private static bool TryPluginArray(JsonElement json, out PluginValue.Array value)
    {
        var items = ImmutableArray.CreateBuilder<PluginValue>();
        foreach (var item in json.EnumerateArray())
        {
            if (!TryPluginValue(item, out var parsed))
            {
                value = null!;
                return false;
            }
            items.Add(parsed);
        }

        value = new(items.ToImmutable());
        return true;
    }

    private static bool TryPluginMap(JsonElement json, out PluginValue.Map value)
    {
        var properties = ImmutableArray.CreateBuilder<PluginValueProperty>();
        foreach (var property in json.EnumerateObject())
        {
            if (!TryPluginValue(property.Value, out var parsed))
            {
                value = null!;
                return false;
            }
            properties.Add(new(property.Name, parsed));
        }

        value = new(properties.ToImmutable());
        return true;
    }

    private static bool TryArray(PluginValue.Array value, out AutomationValue.Array converted)
    {
        var items = ImmutableArray.CreateBuilder<AutomationValue>();
        foreach (var item in value.Items)
        {
            if (!TryConvert(item, out var parsed))
            {
                converted = null!;
                return false;
            }
            items.Add(parsed);
        }

        converted = new(items.ToImmutable());
        return true;
    }

    private static bool TryMap(PluginValue.Map value, out AutomationValue.Map converted)
    {
        var properties = ImmutableArray.CreateBuilder<AutomationValueProperty>();
        foreach (var property in value.Properties)
        {
            if (!TryConvert(property.Value, out var parsed))
            {
                converted = null!;
                return false;
            }
            properties.Add(new(property.Name, parsed));
        }

        converted = new(properties.ToImmutable());
        return true;
    }

    private static bool TryDecimal(double value, out decimal converted)
    {
        try
        {
            converted = (decimal)value;
            return double.IsFinite(value);
        }
        catch (OverflowException)
        {
            converted = default;
            return false;
        }
    }
}
