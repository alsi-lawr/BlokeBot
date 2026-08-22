using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;

namespace BlokeBot.Core.Features.ConfigurationTransfer;

internal static class ConfigurationConflictIds
{
    public static string CustomCommandAlias(string commandId, string alias) =>
        $"alias:{commandId}:{alias}";

    public static string NormalizeCustomCommandAlias(string alias) =>
        CommandAliasNormalizer.Normalize(alias);

    public static string SelectedCustomCommandAlias(
        string commandId,
        string alias,
        IReadOnlyCollection<ImportItemResolution> resolutions
    ) =>
        NormalizeCustomCommandAlias(
            resolutions.SingleOrDefault(resolution =>
                resolution.ImportedId == CustomCommandAlias(commandId, alias)
            )
                is {
                    Resolution: ImportConflictResolution.Rename,
                    ReplacementName: { Length: > 0 } replacement
                }
                ? replacement
                : alias
        );

    public static bool SkipsCustomCommand(
        CustomCommandV1 command,
        IReadOnlyCollection<ImportItemResolution> resolutions
    ) =>
        resolutions.Any(resolution =>
            resolution.Resolution == ImportConflictResolution.Skip
            && BelongsToCustomCommand(command, resolution.ImportedId)
        );

    public static bool BelongsToCustomCommand(CustomCommandV1 command, string conflictId) =>
        conflictId == command.Id
        || command.Aliases.Any(alias => conflictId == CustomCommandAlias(command.Id, alias));
}
