using System.Text.Json.Serialization;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.ConfigurationTransfer.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OverlaysSectionV1(
    [property: JsonRequired] bool UrlLayersIncluded,
    [property: JsonRequired] bool MediaDocumentLinksIncluded,
    [property: JsonRequired] IReadOnlyList<OverlayInstanceV1> Instances,
    [property: JsonRequired] IReadOnlyList<OverlayMediaReferenceV1> MediaReferences,
    [property: JsonRequired] IReadOnlyList<OverlayCueV1> Cues,
    [property: JsonRequired] IReadOnlyList<string> OmittedCueNames,
    [property: JsonRequired] IReadOnlyList<string> OmittedInstanceNames
);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OverlayInstanceV1(
    [property: JsonRequired] string Id,
    [property: JsonRequired] string Name,
    [property: JsonRequired] OverlayType Type,
    [property: JsonRequired] bool Enabled,
    [property: JsonRequired] OverlayConfigurationV1 Configuration
);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OverlayConfigurationV1(
    [property: JsonRequired] int SchemaVersion,
    OverlayAppearanceV1? Appearance = null,
    bool? ShowGuessCount = null,
    int? ResultDurationSeconds = null,
    string? Title = null,
    bool? ShowEntrantCount = null,
    bool? ShowCountdown = null,
    bool? ShowJoinCommand = null,
    int? Capacity = null,
    EventFeedOverflowPolicy? OverflowPolicy = null,
    IReadOnlyList<OverlayEventFeedKindV1>? EventKinds = null,
    string? ViewerQueueName = null,
    int? CurrentRows = null,
    int? NextRows = null
);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OverlayAppearanceV1(
    [property: JsonRequired] int X,
    [property: JsonRequired] int Y,
    [property: JsonRequired] int Width,
    [property: JsonRequired] int Height,
    [property: JsonRequired] string Css
);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OverlayEventFeedKindV1(
    [property: JsonRequired] OverlayEventFeedKind Kind,
    [property: JsonRequired] bool Enabled,
    [property: JsonRequired] string Template,
    [property: JsonRequired] OverlayEventFeedPriority Priority,
    [property: JsonRequired] int DurationSeconds
);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OverlayMediaReferenceV1(
    [property: JsonRequired] string Id,
    [property: JsonRequired] string Name,
    [property: JsonRequired] Guid DocumentId,
    [property: JsonRequired] string ContentType,
    [property: JsonRequired] long ByteLength
);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OverlayCueV1(
    [property: JsonRequired] string Id,
    [property: JsonRequired] string Name,
    [property: JsonRequired] bool Enabled,
    [property: JsonRequired] int DurationMilliseconds,
    [property: JsonRequired] OverlayCueQueuePolicy QueuePolicy,
    [property: JsonRequired] IReadOnlyList<OverlayCueLayerV1> Layers
);

public enum OverlayCueLayerTypeV1
{
    UploadedMedia,
    RemoteMedia,
    ExternalWeb,
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OverlayCueLayerV1(
    [property: JsonRequired] OverlayCueLayerTypeV1 Type,
    [property: JsonRequired] int StartOffsetMilliseconds,
    [property: JsonRequired] int DurationMilliseconds,
    [property: JsonRequired] int ZIndex,
    string? MediaReferenceId = null,
    string? Url = null,
    OverlayCueMediaKindV1? MediaKind = null,
    decimal? Volume = null,
    OverlayCueFitModeV1? Fit = null,
    OverlayCueRectangleV1? Rectangle = null
);

public enum OverlayCueMediaKindV1
{
    Video,
    Audio,
    Image,
}

public enum OverlayCueFitModeV1
{
    Contain,
    Cover,
    Fill,
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OverlayCueRectangleV1(
    [property: JsonRequired] decimal XPercent,
    [property: JsonRequired] decimal YPercent,
    [property: JsonRequired] decimal WidthPercent,
    [property: JsonRequired] decimal HeightPercent
);
