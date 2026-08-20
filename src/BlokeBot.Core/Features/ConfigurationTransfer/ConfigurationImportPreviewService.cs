using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.ConfigurationTransfer;

public sealed class ConfigurationImportPreviewService(
    IDbContextFactory<BlokeBotDbContext> dbFactory
)
{
    public async Task<ConfigurationPreviewOutcome> PreviewAsync(
        ConfigurationDocumentV1 document,
        ConfigurationImportSelection selection,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var host = await db
            .Hosts.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == selection.DestinationHostId, cancellationToken);
        if (host is null)
        {
            return new ConfigurationPreviewOutcome.HostNotFound();
        }

        var previews = new List<ConfigurationSectionPreview>();
        foreach (var selected in selection.Sections)
        {
            previews.Add(
                await PreviewSectionAsync(db, host, document, selected, cancellationToken)
            );
        }
        var enablement = document.Sections.ChannelToolEnablement is { } imported
            ? ChannelToolEnablementMapper
                .Changes(host.EnabledFeatures, imported)
                .Select(x => new ConfigurationEnablementChange(
                    x.Feature,
                    host.EnabledFeatures.Contains(x.Feature),
                    x.Enabled,
                    selection.EnablementChanges.Contains(x.Feature)
                ))
                .ToArray()
            : [];
        return new ConfigurationPreviewOutcome.Success(
            new(Guid.NewGuid(), host.Id, host.Login, document, previews, enablement)
        );
    }

    private static async Task<ConfigurationSectionPreview> PreviewSectionAsync(
        BlokeBotDbContext db,
        BotHost host,
        ConfigurationDocumentV1 document,
        SectionImportSelection selection,
        CancellationToken cancellationToken
    ) =>
        selection.Section switch
        {
            ConfigurationSectionId.CustomCommands => await PreviewCommandsAsync(
                db,
                host.Id,
                document.Sections.CustomCommands,
                cancellationToken
            ),
            ConfigurationSectionId.Announcements => await PreviewNamesAsync(
                db.CustomAnnouncements.Where(x => x.HostId == host.Id).Select(x => x.Name),
                document.Sections.Announcements?.Items.Select(x => x.Name),
                selection
            ),
            ConfigurationSectionId.Guessing => await PreviewGuessingAsync(
                db,
                host.Id,
                document.Sections.Guessing,
                selection,
                cancellationToken
            ),
            ConfigurationSectionId.Points => await PreviewPointsAsync(
                db,
                host.Id,
                document.Sections.Points,
                cancellationToken
            ),
            ConfigurationSectionId.ChannelToolEnablement => new(
                selection.Section,
                new(0, document.Sections.ChannelToolEnablement is null ? 0 : 1, 0, 0),
                [],
                []
            ),
            _ => new(
                selection.Section,
                new(0, 0, 0, 0),
                [new("sections", "Unsupported section.")],
                []
            ),
        };

    private static async Task<ConfigurationSectionPreview> PreviewCommandsAsync(
        BlokeBotDbContext db,
        int hostId,
        CustomCommandsSectionV1? section,
        CancellationToken cancellationToken
    )
    {
        if (section is null)
        {
            return Missing(ConfigurationSectionId.CustomCommands);
        }

        var existingNames = await db
            .CustomCommands.AsNoTracking()
            .Where(x => x.HostId == hostId)
            .Select(x => x.Name)
            .ToArrayAsync(cancellationToken);
        var requestedAliases = section
            .Commands.SelectMany(x => x.Aliases)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var occupiedFeatureAliases = await db
            .CommandAliases.AsNoTracking()
            .Where(x => x.HostId == hostId && requestedAliases.Contains(x.Alias))
            .Select(x => x.Alias)
            .ToArrayAsync(cancellationToken);
        var existingCommands = await db
            .CustomCommands.AsNoTracking()
            .Where(x => x.HostId == hostId)
            .Select(x => new { x.Id, x.Name })
            .ToArrayAsync(cancellationToken);
        var occupiedCustomAliases = await db
            .CustomCommandAliases.AsNoTracking()
            .Where(x => x.HostId == hostId && requestedAliases.Contains(x.Alias))
            .Select(x => new { x.Alias, x.CustomCommandId })
            .ToArrayAsync(cancellationToken);
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
                ?.Id;
            foreach (var alias in command.Aliases)
            {
                var duplicateInFile = section.Commands.Any(other =>
                    other.Id != command.Id
                    && other.Aliases.Contains(alias, StringComparer.OrdinalIgnoreCase)
                );
                var occupiedCustom = occupiedCustomAliases.Any(x =>
                    string.Equals(x.Alias, alias, StringComparison.OrdinalIgnoreCase)
                    && x.CustomCommandId != matchedId
                );
                if (
                    !FixedChatCommandRoutes.All.Contains(alias)
                    && !occupiedFeatureAliases.Contains(alias, StringComparer.OrdinalIgnoreCase)
                    && !occupiedCustom
                    && !duplicateInFile
                )
                {
                    continue;
                }
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
        var updates = section.Commands.Count(x =>
            existingNames.Contains(x.Name, StringComparer.OrdinalIgnoreCase)
        );
        return new(
            ConfigurationSectionId.CustomCommands,
            new(section.Commands.Count - updates, updates, 0, 0),
            [],
            conflicts
        );
    }

    private static async Task<ConfigurationSectionPreview> PreviewGuessingAsync(
        BlokeBotDbContext db,
        int hostId,
        GuessingSectionV1? section,
        SectionImportSelection selection,
        CancellationToken cancellationToken
    )
    {
        if (section is null)
        {
            return Missing(ConfigurationSectionId.Guessing);
        }

        var existing = await db
            .Profiles.AsNoTracking()
            .Where(x => x.HostId == hostId)
            .Select(x => new
            {
                x.Id,
                x.Name,
                x.Slug,
                HasHistory = x.Rounds.Any(),
            })
            .ToArrayAsync(cancellationToken);
        var importedSlugs = section.Profiles.Select(x => x.Slug).ToHashSet(StringComparer.Ordinal);
        var conflicts =
            selection.Strategy == ImportConflictStrategy.ReplaceSection
                ? existing
                    .Where(x => x.HasHistory && !importedSlugs.Contains(x.Slug))
                    .Select(x => new ConfigurationImportConflict(
                        ConfigurationSectionId.Guessing,
                        $"target-{x.Id}",
                        x.Name,
                        "This absent profile has retained round history and cannot be deleted.",
                        [ImportConflictResolution.Retain, ImportConflictResolution.Abort]
                    ))
                    .ToArray()
                : [];
        var updates = section.Profiles.Count(x => existing.Any(y => y.Slug == x.Slug));
        var remove =
            selection.Strategy == ImportConflictStrategy.ReplaceSection
                ? existing.Count(x => !importedSlugs.Contains(x.Slug) && !x.HasHistory)
                : 0;
        return new(
            ConfigurationSectionId.Guessing,
            new(section.Profiles.Count - updates, updates, 0, remove),
            [],
            conflicts
        );
    }

    private static async Task<ConfigurationSectionPreview> PreviewPointsAsync(
        BlokeBotDbContext db,
        int hostId,
        PointsSectionV1? section,
        CancellationToken cancellationToken
    )
    {
        if (section is null)
        {
            return Missing(ConfigurationSectionId.Points);
        }

        var aliases = section.CommandAliases.SelectMany(x => x.Aliases).ToArray();
        var collision =
            FixedChatCommandRoutes.FindCollision(aliases)
            ?? await db
                .CustomCommandAliases.AsNoTracking()
                .Where(x => x.HostId == hostId && aliases.Contains(x.Alias))
                .Select(x => x.Alias)
                .FirstOrDefaultAsync(cancellationToken);
        return new(
            ConfigurationSectionId.Points,
            new(0, 1, 0, 0),
            collision is null
                ? []
                :
                [
                    new(
                        "sections.points.commandAliases",
                        $"!{collision} is already used by another command."
                    ),
                ],
            []
        );
    }

    private static async Task<ConfigurationSectionPreview> PreviewNamesAsync(
        IQueryable<string> existingQuery,
        IEnumerable<string>? importedNames,
        SectionImportSelection selection
    )
    {
        if (importedNames is null)
        {
            return Missing(selection.Section);
        }

        var existing = await existingQuery.ToArrayAsync();
        var imported = importedNames.ToArray();
        var updates = imported.Count(x => existing.Contains(x, StringComparer.OrdinalIgnoreCase));
        var remove =
            selection.Strategy == ImportConflictStrategy.ReplaceSection
                ? existing.Count(x => !imported.Contains(x, StringComparer.OrdinalIgnoreCase))
                : 0;
        return new(selection.Section, new(imported.Length - updates, updates, 0, remove), [], []);
    }

    private static ConfigurationSectionPreview Missing(ConfigurationSectionId section) =>
        new(
            section,
            new(0, 0, 0, 0),
            [new($"sections.{section}", "The selected section is not present in the file.")],
            []
        );
}

public abstract record ConfigurationPreviewOutcome
{
    private ConfigurationPreviewOutcome() { }

    public sealed record Success(ConfigurationImportPreview Preview) : ConfigurationPreviewOutcome;

    public sealed record HostNotFound : ConfigurationPreviewOutcome;
}
