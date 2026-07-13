namespace BlokeBot.Auth.Sessions;

public enum AuthRole
{
    Admin,
    Bot,
    Moderator,
    Streamer,
}

internal static class AuthRoleCodec
{
    public static string Encode(AuthRole role)
    {
        return role switch
        {
            AuthRole.Admin => "admin",
            AuthRole.Bot => "bot",
            AuthRole.Moderator => "moderator",
            AuthRole.Streamer => "streamer",
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
        };
    }

    public static bool TryDecode(string? value, out AuthRole role)
    {
        role = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim().ToLowerInvariant();
        role = normalized switch
        {
            "admin" => AuthRole.Admin,
            "bot" => AuthRole.Bot,
            "moderator" => AuthRole.Moderator,
            "streamer" => AuthRole.Streamer,
            _ => default,
        };

        return normalized is "admin" or "bot" or "moderator" or "streamer";
    }
}
