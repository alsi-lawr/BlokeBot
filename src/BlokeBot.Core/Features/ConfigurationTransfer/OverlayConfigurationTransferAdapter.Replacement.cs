using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.ConfigurationTransfer;

internal sealed partial class OverlayConfigurationTransferAdapter
{
    private static async Task<IReadOnlyList<ConfigurationImportConflict>> ReplacementConflictsAsync(
        BlokeBotDbContext db,
        int hostId,
        IReadOnlyList<ExistingOverlayReference> existingInstances,
        IReadOnlyList<ExistingOverlayReference> existingCues,
        OverlaysSectionV1 imported,
        CancellationToken cancellationToken
    )
    {
        var instanceNames = imported
            .Instances.Select(value => ConfigurationImportReferencePlan.NormalizeName(value.Name))
            .ToHashSet();
        var cueNames = imported
            .Cues.Select(value => ConfigurationImportReferencePlan.NormalizeName(value.Name))
            .ToHashSet();
        var actions = await db
            .CustomCommands.AsNoTracking()
            .Include(value => value.Action)
            .Where(value => value.HostId == hostId)
            .ToArrayAsync(cancellationToken);
        var automationConfigurations = await db
            .AutomationFlowNodes.AsNoTracking()
            .Where(value => value.Flow.HostId == hostId)
            .Select(value => value.ConfigurationJson)
            .ToArrayAsync(cancellationToken);
        var conflicts = new List<ConfigurationImportConflict>();
        foreach (var instance in existingInstances)
        {
            var id = instance.PublicId;
            var name = instance.Name;
            if (
                instanceNames.Contains(ConfigurationImportReferencePlan.NormalizeName(name))
                || !Referenced(id, actions, automationConfigurations, target: true)
            )
            {
                continue;
            }
            conflicts.Add(ReplacementConflict($"overlay-instance-{id:D}", name, "instance"));
        }
        foreach (var cue in existingCues)
        {
            var id = cue.PublicId;
            var name = cue.Name;
            if (
                cueNames.Contains(ConfigurationImportReferencePlan.NormalizeName(name))
                || !Referenced(id, actions, automationConfigurations, target: false)
            )
            {
                continue;
            }
            conflicts.Add(ReplacementConflict($"overlay-cue-{id:D}", name, "cue"));
        }
        return conflicts;
    }

    private static bool Referenced(
        Guid id,
        IEnumerable<CustomCommand> commands,
        IEnumerable<string> automationConfigurations,
        bool target
    )
    {
        var commandReference = commands.Any(command =>
            command.Action is OverlayCueCustomCommandAction action
            && (target ? action.TargetOverlayPublicId : action.CuePublicId) == id
        );
        return commandReference
            || automationConfigurations.Any(configuration =>
                configuration.Contains(id.ToString(), StringComparison.OrdinalIgnoreCase)
            );
    }

    private static ConfigurationImportConflict ReplacementConflict(
        string id,
        string name,
        string kind
    ) =>
        new(
            ConfigurationSectionId.Overlays,
            id,
            name,
            $"This absent Overlay {kind} is still referenced by destination configuration.",
            [ImportConflictResolution.Retain, ImportConflictResolution.Abort]
        );

    private static async Task RemoveAbsentAsync(
        BlokeBotDbContext db,
        int hostId,
        OverlaysSectionV1 section,
        SectionImportSelection selection,
        CancellationToken cancellationToken
    )
    {
        if (selection.Strategy != ImportConflictStrategy.ReplaceSection)
        {
            return;
        }
        var retained = selection
            .ItemResolutions.Where(value => value.Resolution == ImportConflictResolution.Retain)
            .Select(value => value.ImportedId)
            .ToHashSet(StringComparer.Ordinal);
        var instanceNames = section
            .Instances.Select(value => ConfigurationImportReferencePlan.NormalizeName(value.Name))
            .ToHashSet();
        var cueNames = section
            .Cues.Select(value => ConfigurationImportReferencePlan.NormalizeName(value.Name))
            .ToHashSet();
        var mediaNames = section
            .MediaReferences.Select(value =>
                ConfigurationImportReferencePlan.NormalizeName(value.Name)
            )
            .ToHashSet();
        var instances = await db
            .OverlayInstances.Where(value => value.HostId == hostId)
            .ToArrayAsync(cancellationToken);
        db.OverlayInstances.RemoveRange(
            instances.Where(value =>
                value.Type is not (OverlayType.CommunityGoal or OverlayType.ViewerFundedBounty)
                && !instanceNames.Contains(
                    ConfigurationImportReferencePlan.NormalizeName(value.Name)
                )
                && !retained.Contains($"overlay-instance-{value.PublicId:D}")
            )
        );
        var cues = await db
            .OverlayCues.Where(value => value.HostId == hostId)
            .ToArrayAsync(cancellationToken);
        db.OverlayCues.RemoveRange(
            cues.Where(value =>
                !cueNames.Contains(ConfigurationImportReferencePlan.NormalizeName(value.Name))
                && !retained.Contains($"overlay-cue-{value.PublicId:D}")
            )
        );
        _ = await db.SaveChangesAsync(cancellationToken);
        var usedAssetIds = await db
            .OverlayCueMediaAssetReferences.Where(value => value.HostId == hostId)
            .Select(value => value.AssetId)
            .ToArrayAsync(cancellationToken);
        var media = await db
            .OverlayMediaAssets.Where(value => value.HostId == hostId)
            .ToArrayAsync(cancellationToken);
        db.OverlayMediaAssets.RemoveRange(
            media.Where(value =>
                !mediaNames.Contains(ConfigurationImportReferencePlan.NormalizeName(value.Name))
                && !usedAssetIds.Contains(value.Id)
            )
        );
        _ = await db.SaveChangesAsync(cancellationToken);
    }
}
