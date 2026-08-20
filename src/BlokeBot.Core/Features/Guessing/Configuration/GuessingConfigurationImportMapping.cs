using BlokeBot.Core.Features.ConfigurationTransfer;
using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;

namespace BlokeBot.Core.Features.Guessing.Configuration;

internal static class GuessingConfigurationImportMapping
{
    public static GuessingConfigurationImportMatchPlan Resolve(
        GuessingSectionV1 section,
        SectionImportSelection selection,
        IReadOnlyList<GuessingConfigurationImportTarget> existing
    )
    {
        var importedById = section.Profiles.ToDictionary(x => x.Id, StringComparer.Ordinal);
        var existingById = existing.ToDictionary(x => x.Id);
        var matches = new Dictionary<string, int>(StringComparer.Ordinal);
        var claimedTargetIds = new HashSet<int>();
        var issues = new List<ConfigurationValidationIssue>();

        foreach (
            var mapping in selection.ItemResolutions.Where(x =>
                x.TargetId is not null && importedById.ContainsKey(x.ImportedId)
            )
        )
        {
            if (matches.ContainsKey(mapping.ImportedId))
            {
                issues.Add(
                    new(
                        $"sections.guessing.profiles[{mapping.ImportedId}]",
                        "An imported profile can be mapped only once."
                    )
                );
                continue;
            }
            if (!existingById.TryGetValue(mapping.TargetId!.Value, out var target))
            {
                issues.Add(
                    new(
                        $"sections.guessing.profiles[{mapping.ImportedId}]",
                        "The selected destination profile is not available in this channel."
                    )
                );
                continue;
            }
            if (!claimedTargetIds.Add(target.Id))
            {
                issues.Add(
                    new(
                        $"sections.guessing.profiles[{mapping.ImportedId}]",
                        $"Destination profile '{target.Name}' is already mapped to another imported profile."
                    )
                );
                continue;
            }

            matches[mapping.ImportedId] = target.Id;
        }

        foreach (var imported in section.Profiles.Where(x => !matches.ContainsKey(x.Id)))
        {
            var target = existing.SingleOrDefault(x =>
                !claimedTargetIds.Contains(x.Id)
                && string.Equals(x.Slug, imported.Slug, StringComparison.Ordinal)
            );
            if (target is null)
            {
                continue;
            }

            matches[imported.Id] = target.Id;
            _ = claimedTargetIds.Add(target.Id);
        }

        return new(matches, issues);
    }
}

internal sealed record GuessingConfigurationImportMatchPlan(
    IReadOnlyDictionary<string, int> TargetIds,
    IReadOnlyList<ConfigurationValidationIssue> Issues
);

internal sealed record GuessingConfigurationImportTarget(
    int Id,
    string Name,
    string Slug,
    bool HasHistory
);
