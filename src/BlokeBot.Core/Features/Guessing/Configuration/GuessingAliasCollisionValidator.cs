using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Guessing.Configuration;

internal enum GuessingAliasAssignmentOrigin
{
    Retained,
    Requested,
}

internal sealed record GuessingAliasAssignment(
    int ProfileId,
    AppCommandKind Kind,
    string Alias,
    GuessingAliasAssignmentOrigin Origin
);

internal static class GuessingAliasCollisionValidator
{
    private static readonly IReadOnlySet<AppCommandKind> _shareableKinds =
        new HashSet<AppCommandKind>
        {
            AppCommandKind.Stop,
            AppCommandKind.Win,
            AppCommandKind.Guess,
            AppCommandKind.Guesses,
        };

    public static async Task<GuessingConfigurationSaveFailure.AliasAlreadyUsed?> FindForSaveAsync(
        BlokeBotDbContext db,
        int hostId,
        GuessingConfigurationSaveCommand command,
        CancellationToken cancellationToken
    )
    {
        var retained = await db
            .CommandAliases.AsNoTracking()
            .Where(alias =>
                alias.HostId == hostId
                && alias.GuessRoundProfileId != null
                && alias.GuessRoundProfileId != command.ProfileId
            )
            .Select(alias => new GuessingAliasAssignment(
                alias.GuessRoundProfileId!.Value,
                alias.Kind,
                alias.Alias,
                GuessingAliasAssignmentOrigin.Retained
            ))
            .ToArrayAsync(cancellationToken);
        return await FindForFinalGraphAsync(
            db,
            hostId,
            retained.Concat(Requested(command.ProfileId, command.Aliases)).ToArray(),
            cancellationToken
        );
    }

    public static async Task<GuessingConfigurationSaveFailure.AliasAlreadyUsed?> FindForFinalGraphAsync(
        BlokeBotDbContext db,
        int hostId,
        IReadOnlyList<GuessingAliasAssignment> assignments,
        CancellationToken cancellationToken
    )
    {
        var requestedAliases = assignments
            .Where(static assignment =>
                assignment.Origin == GuessingAliasAssignmentOrigin.Requested
            )
            .Select(static assignment => assignment.Alias)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var fixedCollision = FixedChatCommandRoutes.FindCollision(requestedAliases);
        if (fixedCollision is not null)
        {
            return new GuessingConfigurationSaveFailure.AliasAlreadyUsed(fixedCollision);
        }

        var profileCollision = assignments
            .GroupBy(static assignment => assignment.Alias, StringComparer.Ordinal)
            .Where(static group =>
                group.Any(static assignment =>
                    assignment.Origin == GuessingAliasAssignmentOrigin.Requested
                ) && IsInvalidOverlap(group)
            )
            .Select(static group => group.Key)
            .Order(StringComparer.Ordinal)
            .FirstOrDefault();
        if (profileCollision is not null)
        {
            return new GuessingConfigurationSaveFailure.AliasAlreadyUsed(profileCollision);
        }

        var commandCollision = await db
            .CommandAliases.AsNoTracking()
            .Where(alias =>
                alias.HostId == hostId
                && alias.GuessRoundProfileId == null
                && requestedAliases.Contains(alias.Alias)
            )
            .OrderBy(static alias => alias.Alias)
            .Select(static alias => alias.Alias)
            .FirstOrDefaultAsync(cancellationToken);
        if (commandCollision is not null)
        {
            return new GuessingConfigurationSaveFailure.AliasAlreadyUsed(commandCollision);
        }

        var customCollision = await db
            .CustomCommandAliases.AsNoTracking()
            .Where(alias => alias.HostId == hostId && requestedAliases.Contains(alias.Alias))
            .OrderBy(static alias => alias.Alias)
            .Select(static alias => alias.Alias)
            .FirstOrDefaultAsync(cancellationToken);
        var stagedCustomCollision = db
            .ChangeTracker.Entries<CustomCommandAlias>()
            .Where(static entry => entry.State is EntityState.Added or EntityState.Modified)
            .Select(static entry => entry.Entity)
            .Where(alias => alias.HostId == hostId && requestedAliases.Contains(alias.Alias))
            .Select(static alias => alias.Alias)
            .Order(StringComparer.Ordinal)
            .FirstOrDefault();
        var selectedCustomCollision = new[] { customCollision, stagedCustomCollision }
            .Where(static alias => alias is not null)
            .Order(StringComparer.Ordinal)
            .FirstOrDefault();
        return selectedCustomCollision is null
            ? null
            : new GuessingConfigurationSaveFailure.AliasAlreadyUsed(selectedCustomCollision);
    }

    public static IReadOnlyList<GuessingAliasAssignment> Retained(GuessRoundProfile profile) =>
        profile
            .CommandAliases.Select(alias => new GuessingAliasAssignment(
                profile.Id,
                alias.Kind,
                alias.Alias,
                GuessingAliasAssignmentOrigin.Retained
            ))
            .ToArray();

    public static IReadOnlyList<GuessingAliasAssignment> Requested(
        int profileId,
        GuessingCommandAliases aliases
    ) =>
        aliases
            .ToDrafts()
            .SelectMany(draft =>
                BlokeBot
                    .Commands.CommandAliasNormalizer.Split(draft.Aliases)
                    .Select(alias => new GuessingAliasAssignment(
                        profileId,
                        draft.Kind,
                        alias,
                        GuessingAliasAssignmentOrigin.Requested
                    ))
            )
            .ToArray();

    private static bool IsInvalidOverlap(IEnumerable<GuessingAliasAssignment> assignments)
    {
        var values = assignments.ToArray();
        if (values.Length == 1)
        {
            return false;
        }

        var kinds = values.Select(static assignment => assignment.Kind).Distinct().ToArray();
        return kinds.Length != 1
            || !_shareableKinds.Contains(kinds[0])
            || values.Select(static assignment => assignment.ProfileId).Distinct().Count()
                != values.Length;
    }
}
