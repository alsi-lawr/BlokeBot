using System.Text.Json.Serialization;

namespace BlokeBot.Twitch.Auth;

internal sealed record TwitchAppAccessTokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; init; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; init; }
}
