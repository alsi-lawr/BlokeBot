using System.Text.Json.Serialization;

namespace BlokeBot.Auth.Moderation;

internal sealed class ModeratedChannelsResponse
{
    [JsonPropertyName("data")]
    public ModeratedChannelData[] Data { get; init; } = [];

    [JsonPropertyName("pagination")]
    public Pagination Pagination { get; init; } = new();
}
