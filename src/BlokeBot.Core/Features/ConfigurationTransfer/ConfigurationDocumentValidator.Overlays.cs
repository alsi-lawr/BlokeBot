using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;

namespace BlokeBot.Core.Features.ConfigurationTransfer;

internal static partial class ConfigurationDocumentValidator
{
    private static ConfigurationValidationIssue? ValidateOverlays(OverlaysSectionV1? section)
    {
        if (section is null)
        {
            return null;
        }
        var issue =
            Limit("sections.overlays.instances", section.Instances.Count)
            ?? Limit("sections.overlays.mediaReferences", section.MediaReferences.Count)
            ?? Limit("sections.overlays.cues", section.Cues.Count)
            ?? Limit("sections.overlays.omittedCueNames", section.OmittedCueNames.Count)
            ?? Limit("sections.overlays.omittedInstanceNames", section.OmittedInstanceNames.Count)
            ?? DuplicateIds(
                "sections.overlays.instances",
                section.Instances.Select(value => value.Id)
            )
            ?? DuplicateIds(
                "sections.overlays.mediaReferences",
                section.MediaReferences.Select(value => value.Id)
            )
            ?? DuplicateIds("sections.overlays.cues", section.Cues.Select(value => value.Id));
        if (issue is not null)
        {
            return issue;
        }
        if (!section.MediaDocumentLinksIncluded && section.MediaReferences.Count > 0)
        {
            return new(
                "sections.overlays.mediaReferences",
                "Media references require the media-document link selection."
            );
        }

        var mediaIds = section
            .MediaReferences.Select(value => value.Id)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var media in section.MediaReferences)
        {
            if (
                string.IsNullOrWhiteSpace(media.Id)
                || string.IsNullOrWhiteSpace(media.Name)
                || media.DocumentId == Guid.Empty
                || media.ByteLength <= 0
                || !(
                    media.ContentType.StartsWith("image/", StringComparison.Ordinal)
                    || media.ContentType.StartsWith("audio/", StringComparison.Ordinal)
                    || media.ContentType.StartsWith("video/", StringComparison.Ordinal)
                )
            )
            {
                return new(
                    $"sections.overlays.mediaReferences[{media.Id}]",
                    "A media reference requires an opaque document ID and valid media metadata."
                );
            }
        }

        foreach (var cue in section.Cues)
        {
            var path = $"sections.overlays.cues[{cue.Id}]";
            if (
                string.IsNullOrWhiteSpace(cue.Id)
                || string.IsNullOrWhiteSpace(cue.Name)
                || cue.DurationMilliseconds is < 100 or > 300000
                || cue.Layers.Count is < 1 or > 16
            )
            {
                return new(path, "A cue requires a valid name, duration, and from 1 to 16 layers.");
            }
            foreach (var layer in cue.Layers)
            {
                if (
                    ValidateLayer(path, cue.DurationMilliseconds, layer, mediaIds, section) is
                    { } layerIssue
                )
                {
                    return layerIssue;
                }
            }
        }

        foreach (var instance in section.Instances)
        {
            if (
                string.IsNullOrWhiteSpace(instance.Id)
                || string.IsNullOrWhiteSpace(instance.Name)
                || instance.Type
                    is Persistence.Models.OverlayType.CommunityGoal
                        or Persistence.Models.OverlayType.ViewerFundedBounty
                || instance.Configuration.SchemaVersion != 1
            )
            {
                return new(
                    $"sections.overlays.instances[{instance.Id}]",
                    "The overlay instance is not a portable core Format 1 instance."
                );
            }
        }
        return null;
    }

    private static ConfigurationValidationIssue? ValidateLayer(
        string cuePath,
        int cueDuration,
        OverlayCueLayerV1 layer,
        IReadOnlySet<string> mediaIds,
        OverlaysSectionV1 section
    )
    {
        var path = $"{cuePath}.layers";
        return
            layer.StartOffsetMilliseconds < 0
            || layer.DurationMilliseconds <= 0
            || layer.StartOffsetMilliseconds + layer.DurationMilliseconds > cueDuration
            || layer.Rectangle is null
            ? new(path, "Every layer must fit inside the cue and include a rectangle.")
            : layer.Type switch
            {
                OverlayCueLayerTypeV1.UploadedMedia
                    when !section.MediaDocumentLinksIncluded
                        || layer.MediaReferenceId is null
                        || !mediaIds.Contains(layer.MediaReferenceId)
                        || layer.MediaKind is null
                        || layer.Volume is null
                        || layer.Fit is null => new(
                    path,
                    "An uploaded-media layer must reference an exported media document link."
                ),
                OverlayCueLayerTypeV1.RemoteMedia
                    when !section.UrlLayersIncluded
                        || layer.Url is null
                        || layer.MediaKind is null
                        || layer.Volume is null
                        || layer.Fit is null
                        || !ValidHttpsUrl(layer.Url) => new(
                    path,
                    "A remote-media layer requires an exported absolute HTTPS URL."
                ),
                OverlayCueLayerTypeV1.ExternalWeb
                    when !section.UrlLayersIncluded
                        || layer.Url is null
                        || !ValidHttpsUrl(layer.Url) => new(
                    path,
                    "An external-web layer requires an exported absolute HTTPS URL."
                ),
                _ => null,
            };
    }

    private static bool ValidHttpsUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
}
