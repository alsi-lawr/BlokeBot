using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlokeBot.Persistence.Models;

public enum CustomCommandArgumentKind
{
    Number,
    Token,
    RemainingText,
}

public sealed record CustomCommandArgumentField(
    string Name,
    CustomCommandArgumentKind Kind,
    decimal? ExclusiveMinimum = null,
    decimal? Maximum = null,
    int? MaximumWords = null
);

public sealed record CustomCommandArgumentSchema(ImmutableArray<CustomCommandArgumentField> Fields)
{
    private static readonly JsonSerializerOptions _options = new()
    {
        Converters = { new JsonStringEnumConverter<CustomCommandArgumentKind>() },
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static string Serialize(CustomCommandArgumentSchema schema) =>
        JsonSerializer.Serialize(schema, _options);

    public static CustomCommandArgumentSchema Deserialize(string json) =>
        JsonSerializer.Deserialize<CustomCommandArgumentSchema>(json, _options)!;
}
