using BlokeBot.Commands;
using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Features.CustomCommands;

public sealed class CustomCommandAliasRegistry
{
    public async Task<string[]> ValidateAsync(
        BlokeBotDbContext db,
        int hostId,
        int? commandId,
        string aliases,
        CancellationToken ct
    )
    {
        var normalized = CommandAliasNormalizer.Split(aliases).ToArray();
        if (normalized.Length == 0)
            throw new InvalidOperationException("At least one command alias is required.");

        var duplicate = normalized
            .GroupBy(alias => alias, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1)
            ?.Key;
        if (duplicate is not null)
            throw new InvalidOperationException($"Alias !{duplicate} is used more than once.");

        var builtInCollision = await db
            .CommandAliases.AsNoTracking()
            .Where(x => x.HostId == hostId && normalized.Contains(x.Alias))
            .Select(x => x.Alias)
            .FirstOrDefaultAsync(ct);
        if (!string.IsNullOrWhiteSpace(builtInCollision))
            throw new InvalidOperationException(
                $"Alias !{builtInCollision} is already used by another bot function."
            );

        var customCollision = await db
            .CustomCommandAliases.AsNoTracking()
            .Where(x =>
                x.HostId == hostId
                && normalized.Contains(x.Alias)
                && (commandId == null || x.CustomCommandId != commandId.Value)
            )
            .Select(x => x.Alias)
            .FirstOrDefaultAsync(ct);
        if (!string.IsNullOrWhiteSpace(customCollision))
            throw new InvalidOperationException(
                $"Alias !{customCollision} is already used by another custom command."
            );

        return normalized;
    }
}
