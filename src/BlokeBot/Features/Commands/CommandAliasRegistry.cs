using BlokeBot.Commands;
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
        var draftArray = drafts.ToArray();
        var rows = draftArray
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

        var genericDrafts = draftArray
            .Select(x => new BlokeBot.Commands.CommandAliasDraft<AppCommandKind>(x.Kind, x.Aliases))
            .ToArray();
        var duplicate = CommandAliasPolicy.FindDuplicateAlias(genericDrafts);
        if (duplicate is not null)
            throw new InvalidOperationException($"Alias !{duplicate} is used more than once.");

        var owned = ownedKinds.ToArray();
        var requestedAliases = rows.Select(x => x.Alias).ToArray();
        var existingAliases = await db
            .CommandAliases.AsNoTracking()
            .Where(x => x.HostId == hostId && !owned.Contains(x.Kind))
            .Where(x => requestedAliases.Contains(x.Alias))
            .Select(x => new CommandAliasOwnership<AppCommandKind>(x.Kind, x.Alias))
            .ToListAsync(ct);
        var existingCollision = CommandAliasPolicy.FindCollision(
            genericDrafts,
            ownedKinds,
            existingAliases
        );
        if (!string.IsNullOrWhiteSpace(existingCollision))
            throw new InvalidOperationException(
                $"Alias !{existingCollision} is already used by another bot function."
            );

        db.CommandAliases.RemoveRange(
            db.CommandAliases.Where(x => x.HostId == hostId && owned.Contains(x.Kind))
        );
        db.CommandAliases.AddRange(rows);
    }

    public static string JoinAliases(IEnumerable<CommandAlias> aliases, AppCommandKind kind) =>
        string.Join(", ", aliases.Where(x => x.Kind == kind).Select(x => x.Alias).Order());
}

public sealed record CommandAliasDraft(AppCommandKind Kind, string Aliases);
