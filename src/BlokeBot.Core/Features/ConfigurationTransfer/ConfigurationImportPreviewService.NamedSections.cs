using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.ConfigurationTransfer;

public sealed partial class ConfigurationImportPreviewService
{
    private static async Task<ConfigurationSectionPreview> PreviewCommandsAsync(
        BlokeBotDbContext db,
        int hostId,
        CustomCommandsSectionV1? section,
        SectionImportSelection selection,
        CancellationToken cancellationToken
    )
    {
        if (section is null)
        {
            return Missing(ConfigurationSectionId.CustomCommands);
        }

        var existingCommands = await db
            .CustomCommands.AsNoTracking()
            .Where(x => x.HostId == hostId)
            .Select(x => new { x.Id, x.Name })
            .ToArrayAsync(cancellationToken);
        var destinationTimeZoneId = await db
            .Hosts.Where(x => x.Id == hostId)
            .Select(x => x.TimeZoneId)
            .SingleAsync(cancellationToken);
        var conflictCommands =
            selection.Strategy == ImportConflictStrategy.AddMissing
                ? section
                    .Commands.Where(command =>
                        !existingCommands.Any(existing =>
                            string.Equals(
                                existing.Name,
                                command.Name,
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                    )
                    .ToArray()
                : section.Commands;
        var requestedAliases = conflictCommands
            .SelectMany(x => x.Aliases)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var occupiedFeatureAliases = await db
            .CommandAliases.AsNoTracking()
            .Where(x => x.HostId == hostId && requestedAliases.Contains(x.Alias))
            .Select(x => x.Alias)
            .ToArrayAsync(cancellationToken);
        var occupiedCustomAliases = await db
            .CustomCommandAliases.AsNoTracking()
            .Where(x => x.HostId == hostId && requestedAliases.Contains(x.Alias))
            .Select(x => new { x.Alias, x.CustomCommandId })
            .ToArrayAsync(cancellationToken);
        var conflicts = CommandConflicts(
            section with
            {
                Commands = conflictCommands,
            },
            existingCommands.Select(x => (x.Id, x.Name)).ToArray(),
            occupiedFeatureAliases,
            occupiedCustomAliases.Select(x => (x.Alias, x.CustomCommandId)).ToArray()
        );
        var skipped = selection
            .ItemResolutions.Where(x => x.Resolution == ImportConflictResolution.Skip)
            .Select(x => x.ImportedId)
            .ToHashSet(StringComparer.Ordinal);
        var imported = section.Commands.Where(x => !skipped.Contains(x.Id)).ToArray();
        var counts = CountsForNames(
            existingCommands.Select(x => x.Name),
            imported.Select(x => x.Name),
            selection.Strategy
        );
        var retainedBySkip =
            selection.Strategy == ImportConflictStrategy.ReplaceSection
                ? section.Commands.Count(command =>
                    skipped.Contains(command.Id)
                    && existingCommands.Any(existing =>
                        string.Equals(
                            existing.Name,
                            command.Name,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                )
                : 0;
        var timeZoneUpdate =
            selection.Strategy != ImportConflictStrategy.AddMissing
            && !string.Equals(destinationTimeZoneId, section.TimeZoneId, StringComparison.Ordinal)
                ? 1
                : 0;
        return new(
            ConfigurationSectionId.CustomCommands,
            counts with
            {
                Update = counts.Update + timeZoneUpdate,
                Skip = counts.Skip + section.Commands.Count - imported.Length,
                Remove = counts.Remove - retainedBySkip,
            },
            [],
            conflicts
        );
    }

    private static List<ConfigurationImportConflict> CommandConflicts(
        CustomCommandsSectionV1 section,
        IReadOnlyList<(int Id, string Name)> existingCommands,
        IReadOnlyList<string> occupiedFeatureAliases,
        IReadOnlyList<(string Alias, int CustomCommandId)> occupiedCustomAliases
    )
    {
        var conflicts = section
            .Commands.Where(x =>
                x.Action.Type
                    is CustomCommandActionTypeV1.Automation
                        or CustomCommandActionTypeV1.OverlayCue
            )
            .Select(x => new ConfigurationImportConflict(
                ConfigurationSectionId.CustomCommands,
                x.Id,
                x.Name,
                $"This command uses an unsupported {x.Action.Type} dependency.",
                [ImportConflictResolution.Skip, ImportConflictResolution.Abort]
            ))
            .ToList();
        foreach (var command in section.Commands)
        {
            var matchedId = existingCommands
                .SingleOrDefault(x =>
                    string.Equals(x.Name, command.Name, StringComparison.OrdinalIgnoreCase)
                )
                .Id;
            foreach (var alias in command.Aliases)
            {
                var occupied =
                    FixedChatCommandRoutes.All.Contains(alias)
                    || occupiedFeatureAliases.Contains(alias, StringComparer.OrdinalIgnoreCase)
                    || occupiedCustomAliases.Any(x =>
                        string.Equals(x.Alias, alias, StringComparison.OrdinalIgnoreCase)
                        && x.CustomCommandId != matchedId
                    )
                    || section.Commands.Any(other =>
                        other.Id != command.Id
                        && other.Aliases.Contains(alias, StringComparer.OrdinalIgnoreCase)
                    );
                if (occupied)
                {
                    conflicts.Add(
                        new(
                            ConfigurationSectionId.CustomCommands,
                            ConfigurationConflictIds.CustomCommandAlias(command.Id, alias),
                            $"!{alias} on {command.Name}",
                            "This alias is already used by a built-in, another feature, or another custom command.",
                            [
                                ImportConflictResolution.Rename,
                                ImportConflictResolution.Skip,
                                ImportConflictResolution.Abort,
                            ]
                        )
                    );
                }
            }
        }
        return conflicts;
    }
}
