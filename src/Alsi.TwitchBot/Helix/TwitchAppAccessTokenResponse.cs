using System.Text.Json.Serialization;

namespace Alsi.TwitchBot;

internal sealed record TwitchAppAccessTokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; init; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; init; }
}
