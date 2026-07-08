using System.Text.Json.Serialization;

namespace BlokeBot.Auth.Moderation;

internal sealed class Pagination
{
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }
}
