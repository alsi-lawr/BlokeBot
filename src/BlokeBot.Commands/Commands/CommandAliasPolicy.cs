namespace BlokeBot.Commands;

/// <summary>
/// Validates command alias drafts independently of persistence.
/// </summary>
public static class CommandAliasPolicy
{
    public static string? FindDuplicateAlias<TKey>(IEnumerable<CommandAliasDraft<TKey>> drafts)
        where TKey : notnull
    {
        var duplicate = drafts
            .SelectMany(draft =>
                CommandAliasNormalizer
                    .Split(draft.Aliases)
                    .Select(alias => new { draft.Kind, alias })
            )
            .GroupBy(x => x.alias, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(x => x.Count() > 1);

        return duplicate?.Key;
    }

    public static string? FindCollision<TKey>(
        IEnumerable<CommandAliasDraft<TKey>> drafts,
        IReadOnlySet<TKey> ownedKinds,
        IEnumerable<CommandAliasOwnership<TKey>> existingAliases
    )
        where TKey : notnull
    {
        var requestedAliases = drafts
            .SelectMany(draft => CommandAliasNormalizer.Split(draft.Aliases))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return existingAliases
            .Where(alias => !ownedKinds.Contains(alias.Kind))
            .Select(alias => alias.Alias)
            .FirstOrDefault(alias => requestedAliases.Contains(alias));
    }
}

public sealed record CommandAliasDraft<TKey>(TKey Kind, string Aliases)
    where TKey : notnull;

public sealed record CommandAliasOwnership<TKey>(TKey Kind, string Alias)
    where TKey : notnull;
