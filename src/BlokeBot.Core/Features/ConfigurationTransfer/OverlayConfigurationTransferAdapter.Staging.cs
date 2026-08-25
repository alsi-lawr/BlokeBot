using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.ConfigurationTransfer;

internal sealed partial class OverlayConfigurationTransferAdapter
{
    private async Task StageMediaAsync(
        BlokeBotDbContext db,
        int hostId,
        OverlaysSectionV1 section,
        SectionImportSelection selection,
        ConfigurationImportReferencePlan references,
        CancellationToken cancellationToken
    )
    {
        var existing = await db
            .OverlayMediaAssets.Include(value => value.Document)
            .Where(value => value.HostId == hostId)
            .ToArrayAsync(cancellationToken);
        foreach (var imported in section.MediaReferences)
        {
            var target = references.OverlayMedia[imported.Id];
            var asset = existing.SingleOrDefault(value => value.PublicId == target.ReferenceId);
            if (asset is not null && selection.Strategy == ImportConflictStrategy.AddMissing)
            {
                continue;
            }
            if (asset is null)
            {
                asset = new OverlayMediaAsset
                {
                    PublicId = target.ReferenceId,
                    HostId = hostId,
                    Name = imported.Name.Trim(),
                    ContentRevision = 1,
                    DocumentId = target.Document.Id,
                    CreatedAtUtc = Now(),
                    UpdatedAtUtc = Now(),
                };
                _ = db.OverlayMediaAssets.Add(asset);
            }
            else
            {
                asset.Name = imported.Name.Trim();
                if (asset.DocumentId != target.Document.Id)
                {
                    asset.DocumentId = target.Document.Id;
                    asset.ContentRevision++;
                }
                asset.UpdatedAtUtc = Now();
            }
        }
        _ = await db.SaveChangesAsync(cancellationToken);
    }

    private async Task StageInstancesAsync(
        BlokeBotDbContext db,
        int hostId,
        OverlaysSectionV1 section,
        SectionImportSelection selection,
        ConfigurationImportReferencePlan references,
        CancellationToken cancellationToken
    )
    {
        var existing = await db
            .OverlayInstances.Where(value => value.HostId == hostId)
            .ToDictionaryAsync(value => value.PublicId, cancellationToken);
        foreach (var imported in section.Instances)
        {
            var configuration = await OverlayConfigurationTransferMapper.MapAsync(
                db,
                hostId,
                imported,
                cancellationToken
            );
            var mapped = ((OverlayConfigurationMapOutcome.Mapped)configuration).Configuration;
            var id = references.OverlayInstances[imported.Id];
            if (existing.TryGetValue(id, out var instance))
            {
                if (selection.Strategy == ImportConflictStrategy.AddMissing)
                {
                    continue;
                }
                instance.Name = imported.Name.Trim();
                instance.Type = imported.Type;
                instance.IsEnabled = imported.Enabled;
                instance.ConfigurationJson = mapped.ToPersistenceJson();
                instance.Revision++;
                instance.UpdatedAtUtc = Now();
            }
            else
            {
                instance = new OverlayInstance
                {
                    PublicId = id,
                    HostId = hostId,
                    Name = imported.Name.Trim(),
                    Type = imported.Type,
                    IsEnabled = imported.Enabled,
                    ConfigurationJson = mapped.ToPersistenceJson(),
                    AccessKeyDigest = OverlayAccessKeyDigest.CreateUnavailablePlaceholder(id),
                    RequiresAccessKeyRegeneration = true,
                    KeyVersion = 1,
                    Revision = 1,
                    CreatedAtUtc = Now(),
                    UpdatedAtUtc = Now(),
                };
                _ = db.OverlayInstances.Add(instance);
            }
        }
        _ = await db.SaveChangesAsync(cancellationToken);
    }

    private async Task StageCuesAsync(
        BlokeBotDbContext db,
        int hostId,
        OverlaysSectionV1 section,
        SectionImportSelection selection,
        ConfigurationImportReferencePlan references,
        CancellationToken cancellationToken
    )
    {
        var existing = await db
            .OverlayCues.Where(value => value.HostId == hostId)
            .ToDictionaryAsync(value => value.PublicId, cancellationToken);
        var assets = await db
            .OverlayMediaAssets.Where(value => value.HostId == hostId)
            .ToDictionaryAsync(value => value.PublicId, cancellationToken);
        foreach (var imported in section.Cues)
        {
            var id = references.OverlayCues[imported.Id];
            if (
                existing.TryGetValue(id, out var existingCue)
                && selection.Strategy == ImportConflictStrategy.AddMissing
            )
            {
                continue;
            }
            var configuration = OverlayCueConfiguration.Create(
                imported.Layers.Select(layer => Layer(layer, references)).ToArray()
            );
            var mapped = ((OverlayCueConfigurationResult.Valid)configuration).Value;
            var cue = existingCue;
            if (cue is null)
            {
                cue = new OverlayCue
                {
                    PublicId = id,
                    HostId = hostId,
                    Revision = 1,
                    CreatedAtUtc = Now(),
                };
                _ = db.OverlayCues.Add(cue);
            }
            else
            {
                cue.Revision++;
                var oldReferences = await db
                    .OverlayCueMediaAssetReferences.Where(value => value.CueId == cue.Id)
                    .ToArrayAsync(cancellationToken);
                db.OverlayCueMediaAssetReferences.RemoveRange(oldReferences);
            }
            cue.Name = imported.Name.Trim();
            cue.IsEnabled = imported.Enabled;
            cue.DurationMilliseconds = imported.DurationMilliseconds;
            cue.QueuePolicy = imported.QueuePolicy;
            cue.ConfigurationJson = mapped.ToPersistenceJson();
            cue.UpdatedAtUtc = Now();
            _ = await db.SaveChangesAsync(cancellationToken);
            foreach (var assetId in mapped.ReferencedAssetIds)
            {
                var asset = assets[assetId];
                _ = db.OverlayCueMediaAssetReferences.Add(
                    new()
                    {
                        CueId = cue.Id,
                        AssetId = asset.Id,
                        HostId = hostId,
                    }
                );
            }
            _ = await db.SaveChangesAsync(cancellationToken);
        }
    }

    private static OverlayCueLayer Layer(
        OverlayCueLayerV1 value,
        ConfigurationImportReferencePlan references
    )
    {
        var rectangle = value.Rectangle!;
        var mappedRectangle = new OverlayCueRectangle(
            rectangle.XPercent,
            rectangle.YPercent,
            rectangle.WidthPercent,
            rectangle.HeightPercent
        );
        return value.Type switch
        {
            OverlayCueLayerTypeV1.UploadedMedia => new OverlayCueLayer.UploadedMedia
            {
                AssetId = references.OverlayMedia[value.MediaReferenceId!].ReferenceId,
                MediaKind = (OverlayCueMediaKind)value.MediaKind!.Value,
                Volume = value.Volume!.Value,
                Fit = (OverlayCueFitMode)value.Fit!.Value,
                Rectangle = mappedRectangle,
                StartOffsetMilliseconds = value.StartOffsetMilliseconds,
                DurationMilliseconds = value.DurationMilliseconds,
                ZIndex = value.ZIndex,
            },
            OverlayCueLayerTypeV1.RemoteMedia => new OverlayCueLayer.RemoteMedia
            {
                Url = new(value.Url!),
                MediaKind = (OverlayCueMediaKind)value.MediaKind!.Value,
                Volume = value.Volume!.Value,
                Fit = (OverlayCueFitMode)value.Fit!.Value,
                Rectangle = mappedRectangle,
                StartOffsetMilliseconds = value.StartOffsetMilliseconds,
                DurationMilliseconds = value.DurationMilliseconds,
                ZIndex = value.ZIndex,
            },
            OverlayCueLayerTypeV1.ExternalWeb => new OverlayCueLayer.ExternalWeb
            {
                Url = new(value.Url!),
                Rectangle = mappedRectangle,
                StartOffsetMilliseconds = value.StartOffsetMilliseconds,
                DurationMilliseconds = value.DurationMilliseconds,
                ZIndex = value.ZIndex,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(value.Type)),
        };
    }

    private DateTime Now() => timeProvider.GetUtcNow().UtcDateTime;
}
