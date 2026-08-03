namespace BlokeBot.Twitch;

public static class Login
{
    public static string Normalize(string? value) =>
        (value ?? string.Empty).Trim().TrimStart('#', '@').ToLowerInvariant();

    public static string[] NormalizeMany(IEnumerable<string?> values) =>
        values
            .Select(Normalize)
            .Where(static login => login.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static login => login, StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
