using System.Collections.Immutable;
using System.Text.Json;

namespace BlokeBot.Core.Features.Overlays.Full;

internal sealed record FullOverlayWidgetFrame(
    Guid Id,
    FullOverlayAudio Audio,
    string Kind,
    JsonElement Content
);

internal sealed record FullOverlayRenderFrame(
    Guid ConnectionId,
    long Generation,
    string Html,
    string Css,
    ImmutableArray<FullOverlayWidgetFrame> Widgets
);

internal static class FullOverlayPublicSnapshots
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    internal static JsonElement Json<T>(T value) => JsonSerializer.SerializeToElement(value, _json);

    internal static JsonElement Source(OverlaySnapshotProjection projection) =>
        projection switch
        {
            OverlaySnapshotProjection.EmptyV1 value => Json(value.Snapshot),
            OverlaySnapshotProjection.GuessingV1 value => Json(value.Snapshot),
            OverlaySnapshotProjection.CuePlayerV1 value => Json(value.Snapshot),
            OverlaySnapshotProjection.GiveawayV1 value => Json(value.Snapshot),
            OverlaySnapshotProjection.EventFeedV1 value => Json(value.Snapshot),
            OverlaySnapshotProjection.ViewerQueueV1 value => Json(value.Snapshot),
            OverlaySnapshotProjection.CommunityGoalV1 value => Json(value.Snapshot),
            OverlaySnapshotProjection.ViewerFundedBountyV1 value => Json(value.Snapshot),
            OverlaySnapshotProjection.Unavailable => Json(new { unavailable = true }),
            _ => throw new InvalidOperationException("Unknown overlay snapshot projection."),
        };
}
