using System.Text.Json.Serialization;

namespace BlokeBot.Auth.Users;

internal sealed class UserResponse
{
    [JsonPropertyName("data")]
    public UserData[] Data { get; init; } = [];
}
