namespace BlokeBot.Twitch;

public static class TwitchLogin
{
    public static string Normalize(string? value)
    {
        return (value ?? string.Empty).Trim().TrimStart('#', '@').ToLowerInvariant();
    }

    public static string[] NormalizeMany(IEnumerable<string?> values)
    {
        return values
            .Select(Normalize)
            .Where(login => login.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(login => login, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
