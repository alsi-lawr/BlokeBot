using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlokeBot.Plugins.Contracts;

[JsonConverter(typeof(PluginApiVersionJsonConverter))]
public sealed record PluginApiVersion : IComparable<PluginApiVersion>
{
    private PluginApiVersion(int value) => Value = value;

    public int Value { get; }

    public static PluginApiVersion V1 { get; } = new(1);

    public static bool TryCreate(int candidate, out PluginApiVersion version)
    {
        var valid = candidate > 0;
        version = valid ? new(candidate) : null!;
        return valid;
    }

    public int CompareTo(PluginApiVersion? other) =>
        other is null ? 1 : Value.CompareTo(other.Value);

    public static bool operator <(PluginApiVersion left, PluginApiVersion right) =>
        left.CompareTo(right) < 0;

    public static bool operator <=(PluginApiVersion left, PluginApiVersion right) =>
        left.CompareTo(right) <= 0;

    public static bool operator >(PluginApiVersion left, PluginApiVersion right) =>
        left.CompareTo(right) > 0;

    public static bool operator >=(PluginApiVersion left, PluginApiVersion right) =>
        left.CompareTo(right) >= 0;

    public override string ToString() =>
        Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

internal sealed class PluginApiVersionJsonConverter : JsonConverter<PluginApiVersion>
{
    public override PluginApiVersion Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    ) =>
        (
            reader.TokenType == JsonTokenType.Number
            && reader.TryGetInt32(out var candidate)
            && PluginApiVersion.TryCreate(candidate, out var version)
        )
            ? version
            : throw new JsonException("Invalid plugin API version.");

    public override void Write(
        Utf8JsonWriter writer,
        PluginApiVersion value,
        JsonSerializerOptions options
    ) => writer.WriteNumberValue(value.Value);
}
