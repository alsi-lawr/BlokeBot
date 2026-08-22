using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;

namespace BlokeBot.Core.Features.ConfigurationTransfer;

public sealed partial class ConfigurationImportPreviewService
{
    private static CommandConflictPreview CommandConflicts(
        CustomCommandsSectionV1 section,
        IReadOnlyList<(int Id, string Name)> existingCommands,
        IReadOnlyList<string> occupiedFeatureAliases,
        IReadOnlyList<(string Alias, int CustomCommandId)> occupiedCustomAliases,
        IReadOnlyCollection<ImportItemResolution> resolutions,
        ConfigurationImportReferencePlan references
    )
    {
        var conflicts = section
            .Commands.Where(command =>
                command.Action.Type == CustomCommandActionTypeV1.OverlayCue
                && (
                    command.Action.OverlayTargetId is null
                    || command.Action.OverlayCueId is null
                    || !references.OverlayInstances.ContainsKey(command.Action.OverlayTargetId)
                    || !references.OverlayCues.ContainsKey(command.Action.OverlayCueId)
                )
            )
            .Select(command => new ConfigurationImportConflict(
                ConfigurationSectionId.CustomCommands,
                command.Id,
                command.Name,
                "This command's Overlay target or cue has no explicit destination match.",
                [ImportConflictResolution.Skip, ImportConflictResolution.Abort]
            ))
            .ToList();
        var issues = new List<ConfigurationValidationIssue>();
        var aliasesByCommand = section.Commands.ToDictionary(
            command => command.Id,
            command =>
                command
                    .Aliases.Select(alias => AliasPreview(command.Id, alias, resolutions))
                    .ToArray(),
            StringComparer.Ordinal
        );
        var skippedCommandIds = section
            .Commands.Where(command =>
                ConfigurationConflictIds.SkipsCustomCommand(command, resolutions)
            )
            .Select(command => command.Id)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var command in section.Commands)
        {
            var matchedId = existingCommands
                .SingleOrDefault(x =>
                    string.Equals(x.Name, command.Name, StringComparison.OrdinalIgnoreCase)
                )
                .Id;
            foreach (var alias in aliasesByCommand[command.Id])
            {
                if (
                    IsOccupied(
                        alias.Original,
                        matchedId,
                        command.Id,
                        aliasesByCommand,
                        occupiedFeatureAliases,
                        occupiedCustomAliases,
                        static candidate => candidate.Original,
                        null
                    )
                )
                {
                    conflicts.Add(
                        new(
                            ConfigurationSectionId.CustomCommands,
                            ConfigurationConflictIds.CustomCommandAlias(command.Id, alias.Source),
                            $"!{alias.Original} on {command.Name}",
                            "This alias is already used by a built-in, another feature, or another custom command.",
                            [
                                ImportConflictResolution.Rename,
                                ImportConflictResolution.Skip,
                                ImportConflictResolution.Abort,
                            ]
                        )
                    );
                }
                if (
                    alias.Resolution == ImportConflictResolution.Rename
                    && alias.Selected.Length > 0
                    && !ConfigurationConflictIds.SkipsCustomCommand(command, resolutions)
                    && IsOccupied(
                        alias.Selected,
                        matchedId,
                        command.Id,
                        aliasesByCommand,
                        occupiedFeatureAliases,
                        occupiedCustomAliases,
                        static candidate => candidate.Selected,
                        skippedCommandIds
                    )
                )
                {
                    issues.Add(
                        new(
                            $"sections.customCommands.commands[{command.Id}].aliases",
                            $"!{alias.Selected} is already used by another command. Enter a different alias."
                        )
                    );
                }
            }
        }
        return new(conflicts, issues);
    }

    private static CommandAliasPreview AliasPreview(
        string commandId,
        string alias,
        IReadOnlyCollection<ImportItemResolution> resolutions
    ) =>
        new(
            alias,
            ConfigurationConflictIds.NormalizeCustomCommandAlias(alias),
            ConfigurationConflictIds.SelectedCustomCommandAlias(commandId, alias, resolutions),
            resolutions
                .SingleOrDefault(resolution =>
                    resolution.ImportedId
                    == ConfigurationConflictIds.CustomCommandAlias(commandId, alias)
                )
                ?.Resolution
                ?? ImportConflictResolution.Unresolved
        );

    private static bool IsOccupied(
        string alias,
        int matchedId,
        string commandId,
        IReadOnlyDictionary<string, CommandAliasPreview[]> aliasesByCommand,
        IReadOnlyList<string> occupiedFeatureAliases,
        IReadOnlyList<(string Alias, int CustomCommandId)> occupiedCustomAliases,
        Func<CommandAliasPreview, string> selectedAlias,
        IReadOnlySet<string>? ignoredCommandIds
    ) =>
        FixedChatCommandRoutes.All.Contains(alias)
        || occupiedFeatureAliases.Contains(alias, StringComparer.OrdinalIgnoreCase)
        || occupiedCustomAliases.Any(x =>
            string.Equals(x.Alias, alias, StringComparison.OrdinalIgnoreCase)
            && x.CustomCommandId != matchedId
        )
        || aliasesByCommand.Any(other =>
            other.Key != commandId
            && (ignoredCommandIds is null || !ignoredCommandIds.Contains(other.Key))
            && other.Value.Any(candidate =>
                string.Equals(selectedAlias(candidate), alias, StringComparison.OrdinalIgnoreCase)
            )
        );

    private sealed record CommandConflictPreview(
        IReadOnlyList<ConfigurationImportConflict> Conflicts,
        IReadOnlyList<ConfigurationValidationIssue> Issues
    );

    private sealed record CommandAliasPreview(
        string Source,
        string Original,
        string Selected,
        ImportConflictResolution Resolution
    );
}
