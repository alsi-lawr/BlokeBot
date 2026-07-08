using System.Text.Json.Serialization;

namespace BlokeBot.Auth.Users;

internal sealed class UserData
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("login")]
    public string? Login { get; init; }

    [JsonPropertyName("display_name")]
    public string? DisplayName { get; init; }

    [JsonPropertyName("profile_image_url")]
    public string? ProfileImageUrl { get; init; }
}
