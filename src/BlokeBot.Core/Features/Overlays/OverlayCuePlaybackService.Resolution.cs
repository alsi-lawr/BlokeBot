using System.Collections.Immutable;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Overlays;

internal sealed partial class OverlayCuePlaybackService
{
    private async Task<PlanResolution> ResolvePlanAsync(
        OverlayCueAdmissionRequest request,
        CancellationToken cancellationToken
    )
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var references = await ResolveReferencesAsync(
            db,
            new(request.HostId, request.TargetOverlayId, request.CueId),
            cancellationToken
        );
        if (references is not ReferenceResolution.Available available)
        {
            return references switch
            {
                ReferenceResolution.Disabled { Part: OverlayCueReferencePart.Parent }
                or ReferenceResolution.Missing { Part: OverlayCueReferencePart.Parent } =>
                    new PlanResolution.ParentDisabled(),
                ReferenceResolution.Disabled => new PlanResolution.Disabled(),
                _ => new PlanResolution.Missing(),
            };
        }
        var target = available.Target;
        var cue = available.Cue;

        var parsed = OverlayCueConfiguration.Parse(cue.ConfigurationJson);
        if (parsed is not OverlayCueConfigurationResult.Valid valid)
        {
            logger.LogError(
                "Cue {CueId} for host {HostId} has an invalid persisted configuration.",
                cue.PublicId,
                cue.HostId
            );
            return new PlanResolution.Missing();
        }
        var assetIds = valid.Value.ReferencedAssetIds;
        var assets = await db
            .OverlayMediaAssets.AsNoTracking()
            .Include(value => value.Document)
            .Where(value =>
                value.HostId == request.HostId
                && assetIds.Contains(value.PublicId)
                && value.Document.State == OverlayMediaDocumentState.Available
            )
            .ToDictionaryAsync(value => value.PublicId, cancellationToken);
        if (assets.Count != assetIds.Length)
        {
            return new PlanResolution.Missing();
        }

        foreach (
            var url in valid
                .Value.Layers.Select(layer =>
                    layer switch
                    {
                        OverlayCueLayer.RemoteMedia remote => remote.Url,
                        OverlayCueLayer.ExternalWeb web => web.Url,
                        _ => null,
                    }
                )
                .OfType<Uri>()
        )
        {
            if (
                await urlPolicy.ValidateAsync(url, cancellationToken)
                is OverlayRemoteUrlDecision.Rejected
            )
            {
                return new PlanResolution.Disabled();
            }
        }

        var layers = valid
            .Value.Layers.Select(layer => ResolveLayer(layer, assets))
            .ToImmutableArray();
        var plan = new OverlayCuePlaybackPlan(
            Guid.NewGuid(),
            request.HostId,
            request.TargetOverlayId,
            request.CueId,
            cue.Revision,
            cue.DurationMilliseconds,
            request.Origin,
            request.Context,
            layers
        );
        return new PlanResolution.Ready(
            new ResolvedOverlayInstance(
                target.HostId,
                target.PublicId,
                target.Type,
                OverlayConfiguration.FromPersistence(target.Type, target.ConfigurationJson),
                new OverlayRevision(target.Revision)
            ),
            plan
        );
    }

    private static async Task<ReferenceResolution> ResolveReferencesAsync(
        BlokeBotDbContext db,
        OverlayCueReferenceRequest request,
        CancellationToken cancellationToken
    )
    {
        if (request.HostId <= 0)
        {
            return new ReferenceResolution.Missing(OverlayCueReferencePart.Parent);
        }
        if (request.TargetOverlayId == Guid.Empty)
        {
            return new ReferenceResolution.Missing(OverlayCueReferencePart.Target);
        }
        if (request.CueId == Guid.Empty)
        {
            return new ReferenceResolution.Missing(OverlayCueReferencePart.Cue);
        }

        var features = await db
            .Hosts.AsNoTracking()
            .Where(host => host.Id == request.HostId)
            .Select(host => (HostFeatureFlags?)host.EnabledFeatures)
            .SingleOrDefaultAsync(cancellationToken);
        if (features is null)
        {
            return new ReferenceResolution.Missing(OverlayCueReferencePart.Parent);
        }
        if ((features.Value & HostFeatureFlags.Overlays) != HostFeatureFlags.Overlays)
        {
            return new ReferenceResolution.Disabled(OverlayCueReferencePart.Parent);
        }

        var target = await db
            .OverlayInstances.AsNoTracking()
            .SingleOrDefaultAsync(
                value =>
                    value.HostId == request.HostId
                    && value.PublicId == request.TargetOverlayId
                    && value.Type == OverlayType.CuePlayer,
                cancellationToken
            );
        if (target is null)
        {
            return new ReferenceResolution.Missing(OverlayCueReferencePart.Target);
        }
        if (!target.IsEnabled)
        {
            return new ReferenceResolution.Disabled(OverlayCueReferencePart.Target);
        }

        var cue = await db
            .OverlayCues.AsNoTracking()
            .SingleOrDefaultAsync(
                value => value.HostId == request.HostId && value.PublicId == request.CueId,
                cancellationToken
            );
        return cue switch
        {
            null => new ReferenceResolution.Missing(OverlayCueReferencePart.Cue),
            { IsEnabled: true } => new ReferenceResolution.Available(target, cue),
            _ => new ReferenceResolution.Disabled(OverlayCueReferencePart.Cue),
        };
    }

    private static OverlayCuePlaybackLayer ResolveLayer(
        OverlayCueLayer layer,
        IReadOnlyDictionary<Guid, OverlayMediaAsset> assets
    ) =>
        layer switch
        {
            OverlayCueLayer.UploadedMedia uploaded => new OverlayCuePlaybackLayer.UploadedMedia
            {
                AssetId = uploaded.AssetId,
                ContentRevision = assets[uploaded.AssetId].ContentRevision,
                ContentType = assets[uploaded.AssetId].Document.ContentType,
                Volume = uploaded.Volume,
                Fit = uploaded.Fit,
                Rectangle = uploaded.Rectangle,
                StartOffsetMilliseconds = uploaded.StartOffsetMilliseconds,
                DurationMilliseconds = uploaded.DurationMilliseconds,
                ZIndex = uploaded.ZIndex,
            },
            OverlayCueLayer.RemoteMedia remote => new OverlayCuePlaybackLayer.RemoteMedia
            {
                Url = remote.Url,
                MediaKind = remote.MediaKind,
                Volume = remote.Volume,
                Fit = remote.Fit,
                Rectangle = remote.Rectangle,
                StartOffsetMilliseconds = remote.StartOffsetMilliseconds,
                DurationMilliseconds = remote.DurationMilliseconds,
                ZIndex = remote.ZIndex,
            },
            OverlayCueLayer.ExternalWeb web => new OverlayCuePlaybackLayer.ExternalWeb
            {
                Url = web.Url,
                Rectangle = web.Rectangle,
                StartOffsetMilliseconds = web.StartOffsetMilliseconds,
                DurationMilliseconds = web.DurationMilliseconds,
                ZIndex = web.ZIndex,
            },
            _ => throw new InvalidOperationException("Unsupported Cue-V1 layer."),
        };
}
