using System.Text.Json.Serialization;

namespace BlokeBot.Twitch;

internal sealed class TwitchAccessTokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; init; }
}
