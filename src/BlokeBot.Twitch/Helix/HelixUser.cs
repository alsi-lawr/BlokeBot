using System.Text.Json.Serialization;

namespace BlokeBot.Twitch;

public sealed record HelixUser
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("login")]
    public string Login { get; init; } = string.Empty;

    [JsonPropertyName("display_name")]
    public string DisplayName { get; init; } = string.Empty;

    [JsonPropertyName("profile_image_url")]
    public string ProfileImageUrl { get; init; } = string.Empty;
}
