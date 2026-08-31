using System.Collections.Immutable;
using System.Text.Json;
using Tomlyn.Serialization;

namespace BlokeBot.Plugins.Contracts;

internal sealed class PluginPageActionInputsTomlConverter
    : TomlConverter<ImmutableArray<PluginPageActionInputDescriptor>>
{
    private static readonly IReadOnlyDictionary<string, PluginValueKind> _valueKinds =
        Enum.GetValues<PluginValueKind>()
            .ToDictionary(
                static value => JsonNamingPolicy.CamelCase.ConvertName(value.ToString()),
                static value => value,
                StringComparer.Ordinal
            );

    public override ImmutableArray<PluginPageActionInputDescriptor> Read(TomlReader reader)
    {
        Require(reader, TomlTokenType.StartArray);
        var inputs = ImmutableArray.CreateBuilder<PluginPageActionInputDescriptor>();
        _ = reader.Read();
        while (reader.TokenType != TomlTokenType.EndArray)
        {
            inputs.Add(ReadInput(reader));
        }
        Require(reader, TomlTokenType.EndArray);
        _ = reader.Read();
        return inputs.ToImmutable();
    }

    public override void Write(
        TomlWriter writer,
        ImmutableArray<PluginPageActionInputDescriptor> value
    )
    {
        writer.WriteStartArray();
        foreach (var input in value)
        {
            writer.WriteStartInlineTable();
            writer.WritePropertyName("id");
            writer.WriteStringValue(input.Id.Value);
            writer.WritePropertyName("name");
            writer.WriteStringValue(input.Name);
            writer.WritePropertyName("valueKind");
            writer.WriteStringValue(
                JsonNamingPolicy.CamelCase.ConvertName(input.ValueKind.ToString())
            );
            writer.WritePropertyName("required");
            writer.WriteBooleanValue(input.Required);
            writer.WriteEndInlineTable();
        }
        writer.WriteEndArray();
    }

    private static PluginPageActionInputDescriptor ReadInput(TomlReader reader)
    {
        Require(reader, TomlTokenType.StartTable);
        PluginPageActionInputId? id = null;
        string? name = null;
        PluginValueKind? valueKind = null;
        bool? required = null;
        _ = reader.Read();
        while (reader.TokenType != TomlTokenType.EndTable)
        {
            Require(reader, TomlTokenType.PropertyName);
            var property = reader.PropertyName;
            _ = reader.Read();
            switch (property)
            {
                case "id":
                    id = PluginPageActionInputId.TryCreate(reader.GetString(), out var parsedId)
                        ? parsedId
                        : throw reader.CreateException("Invalid plugin page action input ID.");
                    break;
                case "name":
                    name = reader.GetString();
                    break;
                case "valueKind":
                    valueKind = _valueKinds.TryGetValue(reader.GetString(), out var parsedKind)
                        ? parsedKind
                        : throw reader.CreateException("Invalid plugin page action value kind.");
                    break;
                case "required":
                    required = reader.GetBoolean();
                    break;
                default:
                    throw reader.CreateException(
                        $"Unknown plugin page action input field '{property}'."
                    );
            }
            _ = reader.Read();
        }
        Require(reader, TomlTokenType.EndTable);
        var input =
            id is not null && name is not null && valueKind.HasValue && required.HasValue
                ? new PluginPageActionInputDescriptor(id, name, valueKind.Value, required.Value)
                : throw reader.CreateException("Invalid plugin page action input TOML shape.");
        _ = reader.Read();
        return input;
    }

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
