using System.Text.Json.Serialization;

namespace BlokeBot.Auth.OAuth;

internal sealed class TokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; init; }
}
