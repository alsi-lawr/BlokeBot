using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.ConfigurationTransfer;

internal static partial class ConfigurationExportMappers
{
    internal static async Task<OverlaysSectionV1> OverlaysAsync(
        BlokeBotDbContext db,
        int hostId,
        ConfigurationExportReferencePlan references,
        OverlayExportSelection selection,
        CancellationToken cancellationToken
    )
    {
        var instances = await db
            .OverlayInstances.AsNoTracking()
            .Where(value => value.HostId == hostId)
            .OrderBy(value => value.Name)
            .ThenBy(value => value.PublicId)
            .ToArrayAsync(cancellationToken);
        var queueNames = await db
            .PlayQueues.AsNoTracking()
            .Where(value => value.HostId == hostId)
            .ToDictionaryAsync(value => value.Id, value => value.Name, cancellationToken);
        var portableInstances = new List<OverlayInstanceV1>();
        var omittedInstances = new List<string>();
        foreach (var instance in instances)
        {
            if (instance.Type is OverlayType.CommunityGoal or OverlayType.ViewerFundedBounty)
            {
                omittedInstances.Add(instance.Name);
                continue;
            }
            portableInstances.Add(Instance(instance, references, queueNames));
        }

        var mediaEntities = selection.MediaDocumentLinks
            ? await db
                .OverlayMediaAssets.AsNoTracking()
                .Include(value => value.Document)
                .Where(value => value.HostId == hostId)
                .OrderBy(value => value.Name)
                .ThenBy(value => value.PublicId)
                .ToArrayAsync(cancellationToken)
            : [];
        var media = mediaEntities
            .Select(value => new OverlayMediaReferenceV1(
                references.OverlayMedia[value.PublicId].Id,
                value.Name,
                value.DocumentId,
                value.Document.ContentType,
                value.Document.ByteLength
            ))
            .ToArray();
        var cues = await db
            .OverlayCues.AsNoTracking()
            .Where(value => value.HostId == hostId)
            .OrderBy(value => value.Name)
            .ThenBy(value => value.PublicId)
            .ToArrayAsync(cancellationToken);
        var portableCues = new List<OverlayCueV1>();
        var omittedCues = new List<string>();
        foreach (var cue in cues)
        {
            var parsed = OverlayCueConfiguration.Parse(cue.ConfigurationJson);
            if (parsed is not OverlayCueConfigurationResult.Valid valid)
            {
                throw new InvalidOperationException("Persisted cue configuration is invalid.");
            }
            var layers = valid
                .Value.Layers.Where(layer =>
                    layer switch
                    {
                        OverlayCueLayer.UploadedMedia => selection.MediaDocumentLinks,
                        OverlayCueLayer.RemoteMedia or OverlayCueLayer.ExternalWeb =>
                            selection.UrlLayers,
                        _ => false,
                    }
                )
                .Select(layer => Layer(layer, references))
                .ToArray();
            if (layers.Length == 0)
            {
                omittedCues.Add(cue.Name);
                continue;
            }
            portableCues.Add(
                new(
                    references.OverlayCues[cue.PublicId].Id,
                    cue.Name,
                    cue.IsEnabled,
                    cue.DurationMilliseconds,
                    cue.QueuePolicy,
                    layers
                )
            );
        }

        return new(
            selection.UrlLayers,
            selection.MediaDocumentLinks,
            portableInstances,
            media,
            portableCues,
            omittedCues,
            omittedInstances
        );
    }

    private static OverlayInstanceV1 Instance(
        OverlayInstance value,
        ConfigurationExportReferencePlan references,
        IReadOnlyDictionary<int, string> queueNames
    )
    {
        var configuration = OverlayConfiguration.FromPersistence(
            value.Type,
            value.ConfigurationJson
        );
        return new(
            references.OverlayInstances[value.PublicId].Id,
            value.Name,
            value.Type,
            value.IsEnabled,
            Configuration(configuration, queueNames)
        );
    }

    private static OverlayConfigurationV1 Configuration(
        OverlayConfiguration value,
        IReadOnlyDictionary<int, string> queueNames
    ) =>
        value switch
        {
            OverlayConfiguration.EmptyV1 => new(1),
            OverlayConfiguration.CuePlayerV1 => new(1),
            OverlayConfiguration.GuessingV1 guessing => new(
                1,
                Appearance(guessing.Appearance),
                ShowGuessCount: guessing.ShowGuessCount,
                ResultDurationSeconds: guessing.ResultDurationSeconds
            ),
            OverlayConfiguration.GiveawayV1 giveaway => new(
                1,
                Appearance(giveaway.Appearance),
                Title: giveaway.Title,
                ShowEntrantCount: giveaway.ShowEntrantCount,
                ShowCountdown: giveaway.ShowCountdown,
                ShowJoinCommand: giveaway.ShowJoinCommand
            ),
            OverlayConfiguration.EventFeedV1 feed => new(
                1,
                Appearance(feed.Appearance),
                Capacity: feed.Capacity,
                OverflowPolicy: feed.OverflowPolicy,
                EventKinds:
                [
                    .. feed
                        .Kinds.OrderBy(value => value.Key)
                        .Select(value => new OverlayEventFeedKindV1(
                            value.Key,
                            value.Value.Enabled,
                            value.Value.Template,
                            value.Value.Priority,
                            value.Value.DurationSeconds
                        )),
                ]
            ),
            OverlayConfiguration.ViewerQueueV1 queue
                when queueNames.TryGetValue(queue.QueueId, out var queueName) => new(
                1,
                Appearance(queue.Appearance),
                ViewerQueueName: queueName,
                CurrentRows: queue.CurrentRows,
                NextRows: queue.NextRows
            ),
            OverlayConfiguration.ViewerQueueV1 => throw new InvalidOperationException(
                "A Viewer Queue overlay references a missing queue."
            ),
            _ => throw new InvalidOperationException(
                "The overlay type is not portable in format 1."
            ),
        };

    private static OverlayAppearanceV1 Appearance(OverlayAppearance value) =>
        new(value.X, value.Y, value.Width, value.Height, value.Css);

    private static OverlayCueLayerV1 Layer(
        OverlayCueLayer value,
        ConfigurationExportReferencePlan references
    ) =>
        value switch
        {
            OverlayCueLayer.UploadedMedia media => new(
                OverlayCueLayerTypeV1.UploadedMedia,
                media.StartOffsetMilliseconds,
                media.DurationMilliseconds,
                media.ZIndex,
                references.OverlayMedia[media.AssetId].Id,
                MediaKind: (OverlayCueMediaKindV1)media.MediaKind,
                Volume: media.Volume,
                Fit: (OverlayCueFitModeV1)media.Fit,
                Rectangle: Rectangle(media.Rectangle)
            ),
            OverlayCueLayer.RemoteMedia media => new(
                OverlayCueLayerTypeV1.RemoteMedia,
                media.StartOffsetMilliseconds,
                media.DurationMilliseconds,
                media.ZIndex,
                Url: media.Url.OriginalString,
                MediaKind: (OverlayCueMediaKindV1)media.MediaKind,
                Volume: media.Volume,
                Fit: (OverlayCueFitModeV1)media.Fit,
                Rectangle: Rectangle(media.Rectangle)
            ),
            OverlayCueLayer.ExternalWeb web => new(
                OverlayCueLayerTypeV1.ExternalWeb,
                web.StartOffsetMilliseconds,
                web.DurationMilliseconds,
                web.ZIndex,
                Url: web.Url.OriginalString,
                Rectangle: Rectangle(web.Rectangle)
            ),
            _ => throw new InvalidOperationException("Unsupported overlay cue layer."),
        };

    private static OverlayCueRectangleV1 Rectangle(OverlayCueRectangle value) =>
        new(value.XPercent, value.YPercent, value.WidthPercent, value.HeightPercent);
}
