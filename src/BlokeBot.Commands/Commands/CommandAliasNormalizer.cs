namespace BlokeBot.Commands;

/// <summary>
/// Normalizes chat command aliases for lookup and storage.
/// </summary>
public static class CommandAliasNormalizer
{
    /// <summary>
    /// Normalizes one alias by trimming whitespace, removing a leading bang, and lower-casing.
    /// </summary>
    public static string Normalize(string? value) =>
        (value ?? string.Empty).Trim().TrimStart('!').ToLowerInvariant();

    /// <summary>
    /// Normalizes many aliases, removes blanks and duplicates, and returns a stable ordering.
    /// </summary>
    public static string[] NormalizeMany(IEnumerable<string?> aliases) =>
        aliases
            .Select(Normalize)
            .Where(static x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// Normalizes many aliases while preserving the first occurrence entered by the user.
    /// </summary>
    public static string[] NormalizeManyPreservingOrder(IEnumerable<string?> aliases) =>
        aliases
            .Select(Normalize)
            .Where(static x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    /// <summary>
    /// Splits a comma-separated alias list using the same normalization rules.
    /// </summary>
    public static IReadOnlyList<string> Split(string aliases) =>
        NormalizeMany(
            aliases.Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            )
        );

    /// <summary>
    /// Splits a comma-separated alias list while retaining first-entered normalized order.
    /// </summary>
    public static IReadOnlyList<string> SplitPreservingOrder(string aliases) =>
        NormalizeManyPreservingOrder(
            aliases.Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            )
        );
}
