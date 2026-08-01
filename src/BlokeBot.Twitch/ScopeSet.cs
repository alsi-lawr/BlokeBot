namespace BlokeBot.Twitch;

public static class ScopeSet
{
    public static string Normalize(string? value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant();

    public static string[] NormalizeMany(IEnumerable<string?> scopes) =>
        scopes
            .Select(Normalize)
            .Where(scope => scope.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    public static string Format(IEnumerable<string?> scopes) =>
        string.Join(' ', NormalizeMany(scopes));

    public static string[] Missing(
        IEnumerable<string?> grantedScopes,
        IEnumerable<string?> requiredScopes
    )
    {
        var granted = NormalizeMany(grantedScopes).ToHashSet(StringComparer.Ordinal);
        return NormalizeMany(requiredScopes).Where(scope => !granted.Contains(scope)).ToArray();
    }
}
