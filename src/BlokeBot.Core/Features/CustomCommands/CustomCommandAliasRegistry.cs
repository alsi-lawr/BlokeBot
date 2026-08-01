using System.Diagnostics;
using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.CustomCommands;

public sealed class CustomCommandAliasRegistry
{
    public async Task<CustomCommandAliasConflict?> FindConflictAsync(
        BlokeBotDbContext db,
        int hostId,
        IReadOnlySet<int> excludedCommandIds,
        IReadOnlyCollection<string> aliases,
        CancellationToken ct
    )
    {
        var aliasValues = aliases.ToArray();
        var fixedCollision = FixedChatCommandRoutes.FindCollision(aliasValues);
        if (fixedCollision is not null)
        {
            return new CustomCommandAliasConflict.BuiltIn(fixedCollision);
        }

        var builtInCollision = await db
            .CommandAliases.AsNoTracking()
            .Where(alias => alias.HostId == hostId && aliasValues.Contains(alias.Alias))
            .Select(alias => alias.Alias)
            .FirstOrDefaultAsync(ct);
        if (builtInCollision is not null)
        {
            return new CustomCommandAliasConflict.BuiltIn(builtInCollision);
        }

        var customCollision = await db
            .CustomCommandAliases.AsNoTracking()
            .Where(alias =>
                alias.HostId == hostId
                && aliasValues.Contains(alias.Alias)
                && !excludedCommandIds.Contains(alias.CustomCommandId)
            )
            .Select(alias => alias.Alias)
            .FirstOrDefaultAsync(ct);
        return customCollision is null
            ? null
            : new CustomCommandAliasConflict.Custom(customCollision);
    }
}

public abstract record CustomCommandAliasConflict
{
    private CustomCommandAliasConflict() { }

    public TResult Match<TResult>(Func<BuiltIn, TResult> builtIn, Func<Custom, TResult> custom) =>
        this switch
        {
            BuiltIn value => builtIn(value),
            Custom value => custom(value),
            _ => throw new UnreachableException("Unknown custom command alias conflict."),
        };

    public sealed record BuiltIn(string Alias) : CustomCommandAliasConflict;

    public sealed record Custom(string Alias) : CustomCommandAliasConflict;
}
