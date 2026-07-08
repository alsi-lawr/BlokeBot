using System.Text.Json;
using System.Text.Json.Serialization;
using BlokeBot.Auth.Sessions;

namespace BlokeBot.Hosts;

internal static class BotHostClaimCodec
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string Encode(BotHostChoice host) =>
        JsonSerializer.Serialize(
            new Payload
            {
                Id = host.Id,
                Login = host.Login,
                DisplayName = host.DisplayName,
                Role = AuthRoleCodec.Encode(host.Role),
                ProfileImageUrl = host.ProfileImageUrl,
            },
            JsonOptions
        );

    public static BotHostChoice? Decode(string value)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<Payload>(value, JsonOptions);
            if (
                payload is null
                || payload.Id <= 0
                || string.IsNullOrWhiteSpace(payload.Login)
                || string.IsNullOrWhiteSpace(payload.DisplayName)
                || !AuthRoleCodec.TryDecode(payload.Role, out var role)
            )
            {
                return null;
            }

            return new BotHostChoice(
                payload.Id,
                payload.Login,
                payload.DisplayName,
                role,
                string.IsNullOrWhiteSpace(payload.ProfileImageUrl) ? null : payload.ProfileImageUrl
            );
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static bool Equivalent(BotHostChoice left, BotHostChoice right) =>
        left.Id == right.Id
        && string.Equals(left.Login, right.Login, StringComparison.OrdinalIgnoreCase)
        && string.Equals(left.DisplayName, right.DisplayName, StringComparison.Ordinal)
        && left.Role == right.Role
        && string.Equals(left.ProfileImageUrl, right.ProfileImageUrl, StringComparison.Ordinal);

    private sealed record Payload
    {
        [JsonPropertyName("id")]
        public int Id { get; init; }

        [JsonPropertyName("login")]
        public string Login { get; init; } = string.Empty;

        [JsonPropertyName("displayName")]
        public string DisplayName { get; init; } = string.Empty;

        [JsonPropertyName("role")]
        public string Role { get; init; } = string.Empty;

        [JsonPropertyName("profileImageUrl")]
        public string? ProfileImageUrl { get; init; }
    }
}
