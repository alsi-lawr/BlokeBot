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
    ) =>
        await ValidateExcludingCommandsAsync(
            db,
            hostId,
            commandId is { } id ? new HashSet<int> { id } : new HashSet<int>(),
            aliases,
            ct
        );

    public async Task<string[]> ValidateExcludingCommandsAsync(
        BlokeBotDbContext db,
        int hostId,
        IReadOnlySet<int> excludedCommandIds,
        string aliases,
        CancellationToken ct
    )
    {
        var normalized = CommandAliasNormalizer.Split(aliases).ToArray();
        if (normalized.Length == 0)
            throw new InvalidOperationException("Enter at least one command word.");

        var duplicate = normalized
            .GroupBy(alias => alias, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1)
            ?.Key;
        if (duplicate is not null)
            throw new InvalidOperationException($"!{duplicate} is entered more than once.");

        var builtInCollision = await db
            .CommandAliases.AsNoTracking()
            .Where(x => x.HostId == hostId && normalized.Contains(x.Alias))
            .Select(x => x.Alias)
            .FirstOrDefaultAsync(ct);
        if (!string.IsNullOrWhiteSpace(builtInCollision))
            throw new InvalidOperationException(
                $"!{builtInCollision} is already used by another bot command."
            );

        var customCollision = await db
            .CustomCommandAliases.AsNoTracking()
            .Where(x =>
                x.HostId == hostId
                && normalized.Contains(x.Alias)
                && !excludedCommandIds.Contains(x.CustomCommandId)
            )
            .Select(x => x.Alias)
            .FirstOrDefaultAsync(ct);
        if (!string.IsNullOrWhiteSpace(customCollision))
            throw new InvalidOperationException(
                $"!{customCollision} is already used by another custom command."
            );

        return normalized;
    }
}
