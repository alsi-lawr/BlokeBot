using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Features.Commands;

public sealed class CommandAliasRegistry
{
    public async Task ReplaceAliasesAsync(
        BlokeBotDbContext db,
        int hostId,
        IReadOnlySet<AppCommandKind> ownedKinds,
        IEnumerable<CommandAliasDraft> drafts,
        CancellationToken ct
    )
    {
        var rows = drafts
            .SelectMany(draft =>
                CommandAliasNormalizer
                    .Split(draft.Aliases)
                    .Select(alias => new CommandAlias
                    {
                        HostId = hostId,
                        Kind = draft.Kind,
                        Alias = alias,
                    })
            )
            .ToList();

        var duplicate = rows.GroupBy(x => x.Alias, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(x => x.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"Alias !{duplicate.Key} is used more than once.");

        var owned = ownedKinds.ToArray();
        var existingAliases = rows.Select(x => x.Alias).ToArray();
        var existingCollision = await db
            .CommandAliases.AsNoTracking()
            .Where(x => x.HostId == hostId && !owned.Contains(x.Kind))
            .Where(x => existingAliases.Contains(x.Alias))
            .Select(x => x.Alias)
            .FirstOrDefaultAsync(ct);
        if (!string.IsNullOrWhiteSpace(existingCollision))
            throw new InvalidOperationException(
                $"Alias !{existingCollision} is already used by another bot function."
            );

        db.CommandAliases.RemoveRange(
            db.CommandAliases.Where(x => x.HostId == hostId && owned.Contains(x.Kind))
        );
        db.CommandAliases.AddRange(rows);
    }

    public static string JoinAliases(
        IEnumerable<CommandAlias> aliases,
        AppCommandKind kind
    ) =>
        string.Join(
            ", ",
            aliases.Where(x => x.Kind == kind).Select(x => x.Alias).Order()
        );
}

public sealed record CommandAliasDraft(AppCommandKind Kind, string Aliases);
