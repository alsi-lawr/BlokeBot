using System.Collections.Immutable;
using Tomlyn.Serialization;

namespace BlokeBot.Plugins.Contracts;

internal sealed class PluginValueTomlConverter : TomlConverter<PluginValue>
{
    public override PluginValue Read(TomlReader reader)
    {
        Require(reader, TomlTokenType.StartTable);
        string? kind = null;
        object? scalar = null;
        ImmutableArray<PluginValue>? items = null;
        ImmutableArray<PluginValueProperty>? properties = null;
        while (reader.Read() && reader.TokenType != TomlTokenType.EndTable)
        {
            Require(reader, TomlTokenType.PropertyName);
            var name = reader.PropertyName;
            _ = reader.Read();
            switch (name)
            {
                case "kind":
                    kind = reader.GetString();
                    break;
                case "value":
                    scalar = ReadScalar(reader);
                    break;
                case "items":
                    items = ReadValues(reader);
                    break;
                case "properties":
                    properties = ReadProperties(reader);
                    break;
                default:
                    throw reader.CreateException($"Unknown plugin value field '{name}'.");
            }
        }

        Require(reader, TomlTokenType.EndTable);
        PluginValue parsed = kind switch
        {
            "nil" when scalar is null && items is null && properties is null =>
                new PluginValue.Nil(),
            "boolean" when scalar is bool value && items is null && properties is null =>
                new PluginValue.Boolean(value),
            "number" when Number(scalar, out var value) && items is null && properties is null =>
                new PluginValue.Number(value),
            "string" when scalar is string value && items is null && properties is null =>
                new PluginValue.String(value),
            "array" when scalar is null && items.HasValue && properties is null =>
                new PluginValue.Array(items.Value),
            "map" when scalar is null && items is null && properties.HasValue =>
                new PluginValue.Map(properties.Value),
            _ => throw reader.CreateException("Invalid plugin value TOML shape."),
        };
        _ = reader.Read();
        return parsed;
    }

    public override void Write(TomlWriter writer, PluginValue value)
    {
        writer.WriteStartInlineTable();
        writer.WritePropertyName("kind");
        writer.WriteStringValue(Kind(value));
        switch (value)
        {
            case PluginValue.Nil:
                break;
            case PluginValue.Boolean boolean:
                writer.WritePropertyName("value");
                writer.WriteBooleanValue(boolean.Value);
                break;
            case PluginValue.Number number:
                writer.WritePropertyName("value");
                writer.WriteFloatValue(number.Value);
                break;
            case PluginValue.String text:
                writer.WritePropertyName("value");
                writer.WriteStringValue(text.Value);
                break;
            case PluginValue.Array array:
                writer.WritePropertyName("items");
                writer.WriteStartArray();
                foreach (var item in array.Items)
                {
                    Write(writer, item);
                }
                writer.WriteEndArray();
                break;
            case PluginValue.Map map:
                writer.WritePropertyName("properties");
                writer.WriteStartArray();
                foreach (var property in map.Properties)
                {
                    writer.WriteStartInlineTable();
                    writer.WritePropertyName("name");
                    writer.WriteStringValue(property.Name);
                    writer.WritePropertyName("value");
                    Write(writer, property.Value);
                    writer.WriteEndInlineTable();
                }
                writer.WriteEndArray();
                break;
            default:
                throw new InvalidOperationException("Unknown plugin value type.");
        }
        writer.WriteEndInlineTable();
    }

    private static object ReadScalar(TomlReader reader) =>
        reader.TokenType switch
        {
            TomlTokenType.Boolean => reader.GetBoolean(),
            TomlTokenType.Integer => reader.GetInt64(),
            TomlTokenType.Float => reader.GetDouble(),
            TomlTokenType.String => reader.GetString(),
            _ => throw reader.CreateException("Invalid plugin value scalar."),
        };

    private static ImmutableArray<PluginValue> ReadValues(TomlReader reader)
    {
        Require(reader, TomlTokenType.StartArray);
        var values = ImmutableArray.CreateBuilder<PluginValue>();
        _ = reader.Read();
        while (reader.TokenType != TomlTokenType.EndArray)
        {
            values.Add(new PluginValueTomlConverter().Read(reader));
        }
        Require(reader, TomlTokenType.EndArray);
        return values.ToImmutable();
    }

    private static ImmutableArray<PluginValueProperty> ReadProperties(TomlReader reader)
    {
        Require(reader, TomlTokenType.StartArray);
        var properties = ImmutableArray.CreateBuilder<PluginValueProperty>();
        _ = reader.Read();
        while (reader.TokenType != TomlTokenType.EndArray)
        {
            properties.Add(ReadProperty(reader));
        }
        Require(reader, TomlTokenType.EndArray);
        return properties.ToImmutable();
    }

    private static PluginValueProperty ReadProperty(TomlReader reader)
    {
        Require(reader, TomlTokenType.StartTable);
        string? name = null;
        PluginValue? value = null;
        _ = reader.Read();
        while (reader.TokenType != TomlTokenType.EndTable)
        {
            Require(reader, TomlTokenType.PropertyName);
            var propertyName = reader.PropertyName;
            _ = reader.Read();
            switch (propertyName)
            {
                case "name":
                    name = reader.GetString();
                    _ = reader.Read();
                    break;
                case "value":
                    value = new PluginValueTomlConverter().Read(reader);
                    break;
                default:
                    throw reader.CreateException(
                        $"Unknown plugin value property field '{propertyName}'."
                    );
            }
        }

        Require(reader, TomlTokenType.EndTable);
        PluginValueProperty property =
            name is not null && value is not null
                ? new(name, value)
                : throw reader.CreateException("Invalid plugin value property TOML shape.");
        _ = reader.Read();
        return property;
    }

    private static bool Number(object? value, out double number)
    {
        switch (value)
        {
            case long integer:
                number = integer;
                return true;
            case double floating:
                number = floating;
                return true;
            default:
                number = default;
                return false;
        }
    }

    private static string Kind(PluginValue value) =>
        value switch
        {
            PluginValue.Nil => "nil",
            PluginValue.Boolean => "boolean",
            PluginValue.Number => "number",
            PluginValue.String => "string",
            PluginValue.Array => "array",
            PluginValue.Map => "map",
            _ => throw new InvalidOperationException("Unknown plugin value type."),
        };

    private static void Require(TomlReader reader, TomlTokenType expected)
    {
        if (reader.TokenType != expected)
        {
            throw reader.CreateException(
                $"Expected TOML token {expected}, received {reader.TokenType}."
            );
        }
    }
}
