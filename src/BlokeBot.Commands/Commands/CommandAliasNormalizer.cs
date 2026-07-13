namespace BlokeBot.Commands;

/// <summary>
/// Normalizes chat command aliases for lookup and storage.
/// </summary>
public static class CommandAliasNormalizer
{
    /// <summary>
    /// Normalizes one alias by trimming whitespace, removing a leading bang, and lower-casing.
    /// </summary>
    public static string Normalize(string? value)
    {
        return (value ?? string.Empty).Trim().TrimStart('!').ToLowerInvariant();
    }

    /// <summary>
    /// Normalizes many aliases, removes blanks and duplicates, and returns a stable ordering.
    /// </summary>
    public static string[] NormalizeMany(IEnumerable<string?> aliases)
    {
        return aliases
            .Select(Normalize)
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// Splits a comma-separated alias list using the same normalization rules.
    /// </summary>
    public static IReadOnlyList<string> Split(string aliases)
    {
        return NormalizeMany(
            aliases.Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            )
        );
    }
}
