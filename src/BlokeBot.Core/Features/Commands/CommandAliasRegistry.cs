using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Commands;

public sealed class CommandAliasRegistry
{
    public async Task ReplaceAliasesAsync(
        BlokeBotDbContext db,
        int hostId,
        IReadOnlySet<AppCommandKind> ownedKinds,
        IEnumerable<CommandAliasDraft> drafts,
        CommandAliasScope scope,
        CancellationToken ct
    )
    {
        var guessRoundProfileId = CommandAliasScopePersistence.ToProfileId(scope);
        var draftArray = drafts.ToArray();
        var owned = ownedKinds.ToArray();
        var rows = draftArray
            .SelectMany(draft =>
                CommandAliasNormalizer
                    .Split(draft.Aliases)
                    .Select(alias => new CommandAlias
                    {
                        HostId = hostId,
                        GuessRoundProfileId = guessRoundProfileId,
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
        {
            throw new InvalidOperationException($"!{duplicate} is entered more than once.");
        }

        var requestedAliases = rows.Select(x => x.Alias).ToArray();
        var existingCollision = await db
            .CommandAliases.AsNoTracking()
            .Where(x => requestedAliases.Contains(x.Alias))
            .Where(x =>
                x.HostId == hostId
                && (!owned.Contains(x.Kind) || x.GuessRoundProfileId != guessRoundProfileId)
            )
            .Select(x => x.Alias)
            .FirstOrDefaultAsync(ct);
        if (!string.IsNullOrWhiteSpace(existingCollision))
        {
            throw new InvalidOperationException(
                $"!{existingCollision} is already used by another bot command."
            );
        }

        db.CommandAliases.RemoveRange(
            db.CommandAliases.Where(x =>
                x.HostId == hostId
                && owned.Contains(x.Kind)
                && x.GuessRoundProfileId == guessRoundProfileId
            )
        );
        db.CommandAliases.AddRange(rows);
    }

    public static string JoinAliases(
        IEnumerable<CommandAlias> aliases,
        AppCommandKind kind,
        CommandAliasScope scope
    )
    {
        var guessRoundProfileId = CommandAliasScopePersistence.ToProfileId(scope);
        return string.Join(
            ", ",
            aliases
                .Where(x => x.Kind == kind && x.GuessRoundProfileId == guessRoundProfileId)
                .Select(x => x.Alias)
                .Order()
        );
    }
}

public sealed record CommandAliasDraft(AppCommandKind Kind, string Aliases);
