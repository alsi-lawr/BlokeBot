namespace BlokeBot.Features.Commands;

public static class CommandAliasNormalizer
{
    public static string Normalize(string? value) =>
        (value ?? string.Empty).Trim().TrimStart('!').ToLowerInvariant();

    public static string[] NormalizeMany(IEnumerable<string?> aliases) =>
        aliases
            .Select(Normalize)
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)
            .ToArray();

    public static IReadOnlyList<string> Split(string aliases) =>
        NormalizeMany(
            aliases.Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            )
        );
}
