using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.ConfigurationTransfer;

public sealed partial class ConfigurationImportPreviewService
{
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
        var matches = section.Profiles.Count(x => existing.Any(y => y.Slug == x.Slug));
        var counts = selection.Strategy switch
        {
            ImportConflictStrategy.AddMissing => new ConfigurationPreviewCount(
                section.Profiles.Count - matches,
                0,
                matches,
                0
            ),
            ImportConflictStrategy.Merge => new(section.Profiles.Count - matches, matches, 0, 0),
            ImportConflictStrategy.ReplaceSection => new(
                section.Profiles.Count - matches,
                matches,
                existing.Count(x => !importedSlugs.Contains(x.Slug) && x.HasHistory),
                existing.Count(x => !importedSlugs.Contains(x.Slug) && !x.HasHistory)
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(selection)),
        };
        return new(ConfigurationSectionId.Guessing, counts, [], conflicts);
    }

    private static async Task<ConfigurationSectionPreview> PreviewPointsAsync(
        BlokeBotDbContext db,
        int hostId,
        PointsSectionV1? section,
        SectionImportSelection selection,
        CancellationToken cancellationToken
    )
    {
        if (section is null)
        {
            return Missing(ConfigurationSectionId.Points);
        }

        var exists = await db.PointsSettings.AnyAsync(x => x.HostId == hostId, cancellationToken);
        if (exists && selection.Strategy == ImportConflictStrategy.AddMissing)
        {
            return new(ConfigurationSectionId.Points, new(0, 0, 1, 0), [], []);
        }

        var aliases = section.CommandAliases.SelectMany(x => x.Aliases).ToArray();
        var collision =
            FixedChatCommandRoutes.FindCollision(aliases)
            ?? await db
                .CustomCommandAliases.AsNoTracking()
                .Where(x => x.HostId == hostId && aliases.Contains(x.Alias))
                .Select(x => x.Alias)
                .FirstOrDefaultAsync(cancellationToken);
        var counts = !exists ? new ConfigurationPreviewCount(1, 0, 0, 0) : new(0, 1, 0, 0);
        return new(
            ConfigurationSectionId.Points,
            counts,
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
        return new(
            selection.Section,
            CountsForNames(existing, importedNames, selection.Strategy),
            [],
            []
        );
    }

    private static ConfigurationPreviewCount CountsForNames(
        IEnumerable<string> existingNames,
        IEnumerable<string> importedNames,
        ImportConflictStrategy strategy
    )
    {
        var existing = existingNames.ToArray();
        var imported = importedNames.ToArray();
        var matches = imported.Count(x => existing.Contains(x, StringComparer.OrdinalIgnoreCase));
        return strategy switch
        {
            ImportConflictStrategy.AddMissing => new(imported.Length - matches, 0, matches, 0),
            ImportConflictStrategy.Merge => new(imported.Length - matches, matches, 0, 0),
            ImportConflictStrategy.ReplaceSection => new(
                imported.Length - matches,
                matches,
                0,
                existing.Count(x => !imported.Contains(x, StringComparer.OrdinalIgnoreCase))
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(strategy), strategy, null),
        };
    }
}
