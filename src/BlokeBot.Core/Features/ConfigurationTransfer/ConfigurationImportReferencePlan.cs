using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.ConfigurationTransfer;

internal sealed record OverlayMediaImportTarget(Guid ReferenceId, OverlayMediaDocument Document);

internal sealed record ConfigurationImportReferencePlan(
    IReadOnlyDictionary<string, Guid> OverlayInstances,
    IReadOnlyDictionary<string, Guid> OverlayCues,
    IReadOnlyDictionary<string, OverlayMediaImportTarget> OverlayMedia,
    IReadOnlyDictionary<string, string> CommandNames,
    IReadOnlyDictionary<string, string> RewardNames,
    IReadOnlyList<ConfigurationValidationIssue> Issues
)
{
    internal static async Task<ConfigurationImportReferencePlan> BuildAsync(
        BlokeBotDbContext db,
        int hostId,
        ConfigurationDocumentV1 document,
        ConfigurationImportSelection selection,
        CancellationToken cancellationToken
    )
    {
        var issues = new List<ConfigurationValidationIssue>();
        var existingOverlays = await db
            .OverlayInstances.AsNoTracking()
            .Where(value => value.HostId == hostId)
            .Select(value => new NamedGuid(value.PublicId, value.Name))
            .ToArrayAsync(cancellationToken);
        var existingCues = await db
            .OverlayCues.AsNoTracking()
            .Where(value => value.HostId == hostId)
            .Select(value => new NamedGuid(value.PublicId, value.Name))
            .ToArrayAsync(cancellationToken);
        var existingMedia = await db
            .OverlayMediaAssets.AsNoTracking()
            .Where(value => value.HostId == hostId)
            .Select(value => new NamedGuid(value.PublicId, value.Name))
            .ToArrayAsync(cancellationToken);
        AddAmbiguities("overlay instances", existingOverlays.Select(value => value.Name), issues);
        AddAmbiguities("overlay cues", existingCues.Select(value => value.Name), issues);
        AddAmbiguities("overlay media", existingMedia.Select(value => value.Name), issues);

        var overlayIds = new Dictionary<string, Guid>(StringComparer.Ordinal);
        var cueIds = new Dictionary<string, Guid>(StringComparer.Ordinal);
        var mediaTargets = new Dictionary<string, OverlayMediaImportTarget>(StringComparer.Ordinal);
        if (
            document.Sections.Overlays is { } overlays
            && selection.Sections.Any(value => value.Section == ConfigurationSectionId.Overlays)
        )
        {
            AddAmbiguities(
                "imported overlay instances",
                overlays.Instances.Select(value => value.Name),
                issues
            );
            AddAmbiguities(
                "imported overlay cues",
                overlays.Cues.Select(value => value.Name),
                issues
            );
            AddAmbiguities(
                "imported overlay media",
                overlays.MediaReferences.Select(value => value.Name),
                issues
            );
            foreach (var instance in overlays.Instances)
            {
                overlayIds[instance.Id] = Match(existingOverlays, instance.Name) ?? Guid.NewGuid();
            }
            foreach (var cue in overlays.Cues)
            {
                cueIds[cue.Id] = Match(existingCues, cue.Name) ?? Guid.NewGuid();
            }

            var documentIds = overlays
                .MediaReferences.Select(value => value.DocumentId)
                .Distinct()
                .ToArray();
            var availableDocuments = await db
                .OverlayMediaDocuments.Where(value =>
                    documentIds.Contains(value.Id)
                    && value.State == OverlayMediaDocumentState.Available
                )
                .ToDictionaryAsync(value => value.Id, cancellationToken);
            foreach (var media in overlays.MediaReferences)
            {
                if (!availableDocuments.TryGetValue(media.DocumentId, out var available))
                {
                    issues.Add(
                        new(
                            $"sections.overlays.mediaReferences[{media.Id}].documentId",
                            $"Media document '{media.DocumentId}' is not available in this BlokeBot instance."
                        )
                    );
                    continue;
                }
                if (
                    available.ContentType != media.ContentType
                    || available.ByteLength != media.ByteLength
                )
                {
                    issues.Add(
                        new(
                            $"sections.overlays.mediaReferences[{media.Id}]",
                            $"Media document '{media.DocumentId}' metadata does not match this BlokeBot instance."
                        )
                    );
                    continue;
                }
                mediaTargets[media.Id] = new(
                    Match(existingMedia, media.Name) ?? Guid.NewGuid(),
                    available
                );
            }
        }

        if (document.Sections.CustomCommands is { } customCommands)
        {
            foreach (
                var action in customCommands
                    .Commands.Select(value => value.Action)
                    .Where(value => value.Type == CustomCommandActionTypeV1.OverlayCue)
            )
            {
                if (
                    action.OverlayTargetId is { } targetId
                    && !overlayIds.ContainsKey(targetId)
                    && action.OverlayTargetName is { } targetName
                    && Match(existingOverlays, targetName) is { } target
                )
                {
                    overlayIds[targetId] = target;
                }
                if (
                    action.OverlayCueId is { } cueId
                    && !cueIds.ContainsKey(cueId)
                    && action.OverlayCueName is { } cueName
                    && Match(existingCues, cueName) is { } cue
                )
                {
                    cueIds[cueId] = cue;
                }
            }
        }

        var hostReferences = document.Sections.Automations?.HostReferences ?? [];
        foreach (
            var reference in hostReferences.Where(value =>
                value.Kind == AutomationHostReferenceKindV1.OverlayTarget
                && !overlayIds.ContainsKey(value.Id)
            )
        )
        {
            if (Match(existingOverlays, reference.Name) is { } matched)
            {
                overlayIds[reference.Id] = matched;
            }
        }
        foreach (
            var reference in hostReferences.Where(value =>
                value.Kind == AutomationHostReferenceKindV1.OverlayCue
                && !cueIds.ContainsKey(value.Id)
            )
        )
        {
            if (Match(existingCues, reference.Name) is { } matched)
            {
                cueIds[reference.Id] = matched;
            }
        }

        var commandNames = new Dictionary<string, string>(StringComparer.Ordinal);
        if (document.Sections.CustomCommands is { } commands)
        {
            foreach (var command in commands.Commands)
            {
                commandNames[command.Id] = command.Name;
            }
        }
        foreach (
            var reference in hostReferences.Where(value =>
                value.Kind == AutomationHostReferenceKindV1.CustomCommand
            )
        )
        {
            commandNames[reference.Id] = reference.Name;
        }
        var rewardNames = hostReferences
            .Where(value => value.Kind == AutomationHostReferenceKindV1.CustomReward)
            .ToDictionary(value => value.Id, value => value.Name, StringComparer.Ordinal);
        return new(overlayIds, cueIds, mediaTargets, commandNames, rewardNames, issues);
    }

    internal static string NormalizeName(string value) => value.Trim().ToUpperInvariant();

    private static Guid? Match(IEnumerable<NamedGuid> values, string name)
    {
        var normalized = NormalizeName(name);
        var matches = values.Where(value => NormalizeName(value.Name) == normalized).ToArray();
        return matches.Length == 1 ? matches[0].Id : null;
    }

    private static void AddAmbiguities(
        string collection,
        IEnumerable<string> names,
        ICollection<ConfigurationValidationIssue> issues
    )
    {
        foreach (
            var name in names
                .GroupBy(NormalizeName, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.First())
        )
        {
            issues.Add(
                new(
                    $"sections.{collection.Replace(' ', '.')}",
                    $"'{name}' is ambiguous under the normalized-name matching contract."
                )
            );
        }
    }

    private sealed record NamedGuid(Guid Id, string Name);
}
