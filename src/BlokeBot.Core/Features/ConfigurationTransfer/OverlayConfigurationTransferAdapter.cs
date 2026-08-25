using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BlokeBot.Core.Features.ConfigurationTransfer;

internal sealed partial class OverlayConfigurationTransferAdapter(
    OverlayRemoteUrlPolicy urlPolicy,
    IOptions<BlokeBotOptions> options,
    TimeProvider timeProvider
) : IOverlayConfigurationTransferAdapter
{
    public async Task<ConfigurationSectionPreview> PreviewAsync(
        BlokeBotDbContext db,
        BotHost host,
        OverlaysSectionV1? section,
        SectionImportSelection selection,
        ConfigurationImportReferencePlan references,
        CancellationToken cancellationToken
    )
    {
        if (section is null)
        {
            return new(
                ConfigurationSectionId.Overlays,
                new(0, 0, 0, 0),
                [new("sections.overlays", "The selected section is not present in the file.")],
                []
            );
        }
        var issues = new List<ConfigurationValidationIssue>(references.Issues);
        issues.AddRange(await ValidateAsync(db, host.Id, section, references, cancellationToken));
        var existingInstances = await db
            .OverlayInstances.AsNoTracking()
            .Where(value => value.HostId == host.Id)
            .Select(value => new ExistingOverlayReference(value.PublicId, value.Name))
            .ToArrayAsync(cancellationToken);
        var existingCues = await db
            .OverlayCues.AsNoTracking()
            .Where(value => value.HostId == host.Id)
            .Select(value => new ExistingOverlayReference(value.PublicId, value.Name))
            .ToArrayAsync(cancellationToken);
        var existingMedia = await db
            .OverlayMediaAssets.AsNoTracking()
            .Where(value => value.HostId == host.Id)
            .Select(value => value.Name)
            .ToArrayAsync(cancellationToken);
        var counts = Add(
            Add(
                Counts(
                    existingInstances.Select(value => value.Name),
                    section.Instances.Select(value => value.Name),
                    selection.Strategy
                ),
                Counts(
                    existingCues.Select(value => value.Name),
                    section.Cues.Select(value => value.Name),
                    selection.Strategy
                )
            ),
            Counts(
                existingMedia,
                section.MediaReferences.Select(value => value.Name),
                selection.Strategy
            )
        );
        var conflicts =
            selection.Strategy == ImportConflictStrategy.ReplaceSection
                ? await ReplacementConflictsAsync(
                    db,
                    host.Id,
                    existingInstances,
                    existingCues,
                    section,
                    cancellationToken
                )
                : [];
        var retained = conflicts.Count(conflict =>
            selection.ItemResolutions.Any(resolution =>
                resolution.ImportedId == conflict.ImportedId
                && resolution.Resolution == ImportConflictResolution.Retain
            )
        );
        return new(
            ConfigurationSectionId.Overlays,
            counts with
            {
                Remove = Math.Max(0, counts.Remove - retained),
            },
            issues,
            conflicts
        );
    }

    public async Task<IReadOnlyList<ConfigurationValidationIssue>> StageAsync(
        BlokeBotDbContext db,
        BotHost host,
        OverlaysSectionV1 section,
        SectionImportSelection selection,
        ConfigurationImportReferencePlan references,
        CancellationToken cancellationToken
    )
    {
        var issues = new List<ConfigurationValidationIssue>(references.Issues);
        issues.AddRange(await ValidateAsync(db, host.Id, section, references, cancellationToken));
        if (
            selection.ItemResolutions.Any(value =>
                value.Resolution == ImportConflictResolution.Abort
            )
        )
        {
            issues.Add(new("sections.overlays", "The import was aborted by a conflict decision."));
        }
        if (selection.Strategy == ImportConflictStrategy.ReplaceSection)
        {
            var instances = await db
                .OverlayInstances.AsNoTracking()
                .Where(value => value.HostId == host.Id)
                .Select(value => new ExistingOverlayReference(value.PublicId, value.Name))
                .ToArrayAsync(cancellationToken);
            var cues = await db
                .OverlayCues.AsNoTracking()
                .Where(value => value.HostId == host.Id)
                .Select(value => new ExistingOverlayReference(value.PublicId, value.Name))
                .ToArrayAsync(cancellationToken);
            var conflicts = await ReplacementConflictsAsync(
                db,
                host.Id,
                instances,
                cues,
                section,
                cancellationToken
            );
            if (
                conflicts.Any(conflict =>
                    !selection.ItemResolutions.Any(resolution =>
                        resolution.ImportedId == conflict.ImportedId
                        && resolution.Resolution
                            is ImportConflictResolution.Retain
                                or ImportConflictResolution.Abort
                    )
                )
            )
            {
                issues.Add(
                    new(
                        "sections.overlays",
                        "Resolve every referenced destination Overlay before applying replacement."
                    )
                );
            }
        }
        if (issues.Count > 0)
        {
            return issues;
        }

        await StageMediaAsync(db, host.Id, section, selection, references, cancellationToken);
        await StageInstancesAsync(db, host.Id, section, selection, references, cancellationToken);
        await StageCuesAsync(db, host.Id, section, selection, references, cancellationToken);
        await RemoveAbsentAsync(db, host.Id, section, selection, cancellationToken);
        var logicalBytes = await db
            .OverlayMediaAssets.Where(value => value.HostId == host.Id)
            .Select(value => value.Document)
            .Distinct()
            .SumAsync(value => value.ByteLength, cancellationToken);
        return logicalBytes > options.Value.Overlays.Media.MaximumHostStorageBytes
            ?
            [
                new(
                    "sections.overlays.mediaReferences",
                    "The imported media links exceed the channel media storage quota."
                ),
            ]
            : [];
    }

    private async Task<IReadOnlyList<ConfigurationValidationIssue>> ValidateAsync(
        BlokeBotDbContext db,
        int hostId,
        OverlaysSectionV1 section,
        ConfigurationImportReferencePlan references,
        CancellationToken cancellationToken
    )
    {
        var issues = new List<ConfigurationValidationIssue>();
        foreach (var instance in section.Instances)
        {
            if (
                await OverlayConfigurationTransferMapper.MapAsync(
                    db,
                    hostId,
                    instance,
                    cancellationToken
                )
                is OverlayConfigurationMapOutcome.Invalid invalid
            )
            {
                issues.Add(new($"sections.overlays.instances[{instance.Id}]", invalid.Message));
            }
        }
        foreach (var cue in section.Cues)
        {
            foreach (var layer in cue.Layers)
            {
                if (layer.Url is { } url)
                {
                    var decision = await urlPolicy.ValidateAsync(new(url), cancellationToken);
                    if (decision is OverlayRemoteUrlDecision.Rejected rejected)
                    {
                        issues.Add(
                            new($"sections.overlays.cues[{cue.Id}].layers", rejected.Message)
                        );
                    }
                }
                if (
                    layer.Type == OverlayCueLayerTypeV1.UploadedMedia
                    && (
                        layer.MediaReferenceId is null
                        || !references.OverlayMedia.ContainsKey(layer.MediaReferenceId)
                    )
                )
                {
                    issues.Add(
                        new(
                            $"sections.overlays.cues[{cue.Id}].layers",
                            "An uploaded-media layer has no available destination media reference."
                        )
                    );
                }
            }
            try
            {
                if (
                    OverlayCueConfiguration.Create(
                        cue.Layers.Select(layer => Layer(layer, references)).ToArray()
                    )
                    is OverlayCueConfigurationResult.Invalid invalid
                )
                {
                    issues.Add(new($"sections.overlays.cues[{cue.Id}].layers", invalid.Message));
                }
            }
            catch (Exception exception)
                when (exception
                        is ArgumentException
                            or FormatException
                            or InvalidOperationException
                            or KeyNotFoundException
                )
            {
                issues.Add(
                    new(
                        $"sections.overlays.cues[{cue.Id}].layers",
                        "The cue layers contain an invalid or unresolved value."
                    )
                );
            }
        }
        return issues;
    }

    private static ConfigurationPreviewCount Counts(
        IEnumerable<string> existing,
        IEnumerable<string> imported,
        ImportConflictStrategy strategy
    )
    {
        var current = existing.Select(ConfigurationImportReferencePlan.NormalizeName).ToHashSet();
        var incoming = imported.Select(ConfigurationImportReferencePlan.NormalizeName).ToHashSet();
        return new(
            incoming.Count(name => !current.Contains(name)),
            strategy == ImportConflictStrategy.AddMissing ? 0 : incoming.Count(current.Contains),
            strategy == ImportConflictStrategy.AddMissing ? incoming.Count(current.Contains) : 0,
            strategy == ImportConflictStrategy.ReplaceSection
                ? current.Count(name => !incoming.Contains(name))
                : 0
        );
    }

    private static ConfigurationPreviewCount Add(
        ConfigurationPreviewCount left,
        ConfigurationPreviewCount right
    ) =>
        new(
            left.Add + right.Add,
            left.Update + right.Update,
            left.Skip + right.Skip,
            left.Remove + right.Remove
        );

    private sealed record ExistingOverlayReference(Guid PublicId, string Name);
}
