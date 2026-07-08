namespace BlokeBot.Features.Commands;

public static class CommandAliasNormalizer
{
    public static string Normalize(string? value) =>
        (value ?? string.Empty).Trim().TrimStart('!').ToLowerInvariant();

    public static IReadOnlyList<string> Split(string aliases) =>
        aliases
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Normalize)
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
