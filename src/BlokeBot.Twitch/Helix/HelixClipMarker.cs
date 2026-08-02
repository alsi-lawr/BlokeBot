using System.Text.Json.Serialization;

namespace BlokeBot.Twitch;

public sealed record HelixClipCreate(string Id, string EditUrl);

public sealed record HelixClip(
    string Id,
    string Url,
    string? EditUrl,
    string BroadcasterId,
    string BroadcasterLogin,
    string CreatorId,
    string CreatorLogin,
    string? VideoId
);

public abstract record HelixClipCreateOutcome
{
    private HelixClipCreateOutcome() { }

    public sealed record Created(HelixClipCreate Clip) : HelixClipCreateOutcome;

    public sealed record Offline : HelixClipCreateOutcome;

    public sealed record VodsDisabled : HelixClipCreateOutcome;

    public sealed record RerunOrPremiere : HelixClipCreateOutcome;

    public sealed record Unauthorized : HelixClipCreateOutcome;

    public sealed record Ambiguous : HelixClipCreateOutcome;

    public sealed record ProviderRejected : HelixClipCreateOutcome;
}

public abstract record HelixClipLookupOutcome
{
    private HelixClipLookupOutcome() { }

    public sealed record Found(HelixClip Clip) : HelixClipLookupOutcome;

    public sealed record NotFound : HelixClipLookupOutcome;

    public sealed record Unavailable : HelixClipLookupOutcome;
}

public sealed record HelixStreamMarker(
    string Id,
    string Description,
    int PositionSeconds,
    DateTimeOffset CreatedAt,
    string? Url,
    string? VideoId
);

public abstract record HelixStreamMarkerLookupOutcome
{
    private HelixStreamMarkerLookupOutcome() { }

    public sealed record Found(IReadOnlyList<HelixStreamMarker> Markers)
        : HelixStreamMarkerLookupOutcome;

    public sealed record Unavailable : HelixStreamMarkerLookupOutcome;
}

public abstract record HelixStreamMarkerCreateOutcome
{
    private HelixStreamMarkerCreateOutcome() { }

    public sealed record Created(HelixStreamMarker Marker) : HelixStreamMarkerCreateOutcome;

    public sealed record Offline : HelixStreamMarkerCreateOutcome;

    public sealed record VodsDisabled : HelixStreamMarkerCreateOutcome;

    public sealed record RerunOrPremiere : HelixStreamMarkerCreateOutcome;

    public sealed record Unauthorized : HelixStreamMarkerCreateOutcome;

    public sealed record Ambiguous : HelixStreamMarkerCreateOutcome;

    public sealed record ProviderRejected : HelixStreamMarkerCreateOutcome;
}

internal sealed record HelixClipCreateResponse
{
    [JsonPropertyName("data")]
    public IReadOnlyList<HelixClipCreateWire> Data { get; init; } = [];
}

internal sealed record HelixClipCreateWire
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("edit_url")]
    public string EditUrl { get; init; } = string.Empty;

    public HelixClipCreate ToDomain() => new(Id, EditUrl);
}

internal sealed record HelixClipsResponse
{
    [JsonPropertyName("data")]
    public IReadOnlyList<HelixClipWire> Data { get; init; } = [];
}

internal sealed record HelixClipWire
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; init; } = string.Empty;

    [JsonPropertyName("edit_url")]
    public string? EditUrl { get; init; }

    [JsonPropertyName("broadcaster_id")]
    public string BroadcasterId { get; init; } = string.Empty;

    [JsonPropertyName("broadcaster_login")]
    public string BroadcasterLogin { get; init; } = string.Empty;

    [JsonPropertyName("creator_id")]
    public string CreatorId { get; init; } = string.Empty;

    [JsonPropertyName("creator_name")]
    public string CreatorLogin { get; init; } = string.Empty;

    [JsonPropertyName("video_id")]
    public string? VideoId { get; init; }

    public HelixClip ToDomain() =>
        new(Id, Url, EditUrl, BroadcasterId, BroadcasterLogin, CreatorId, CreatorLogin, VideoId);
}

internal sealed record HelixStreamMarkerResponse
{
    [JsonPropertyName("data")]
    public IReadOnlyList<HelixStreamMarkerWire> Data { get; init; } = [];
}

internal sealed record HelixStreamMarkerWire
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("position_seconds")]
    public int PositionSeconds { get; init; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("URL")]
    public string? Url { get; init; }

    public HelixStreamMarker ToDomain(string? videoId) =>
        new(Id, Description, PositionSeconds, CreatedAt, Url, videoId);
}

internal sealed record HelixStreamMarkersResponse
{
    [JsonPropertyName("data")]
    public IReadOnlyList<HelixStreamMarkersUserWire> Data { get; init; } = [];

    [JsonPropertyName("pagination")]
    public HelixPaginationWire? Pagination { get; init; }
}

internal sealed record HelixPaginationWire
{
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }
}

internal sealed record HelixStreamMarkersUserWire
{
    [JsonPropertyName("videos")]
    public IReadOnlyList<HelixStreamMarkersVideoWire> Videos { get; init; } = [];
}

internal sealed record HelixStreamMarkersVideoWire
{
    [JsonPropertyName("video_id")]
    public string? VideoId { get; init; }

    [JsonPropertyName("markers")]
    public IReadOnlyList<HelixStreamMarkerWire> Markers { get; init; } = [];
}
