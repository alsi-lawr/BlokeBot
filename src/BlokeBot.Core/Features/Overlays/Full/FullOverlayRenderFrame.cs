using System.Collections.Immutable;
using System.Text.Json;

namespace BlokeBot.Core.Features.Overlays.Full;

internal sealed record FullOverlayWidgetFrame(
    Guid Id,
    FullOverlayAudio Audio,
    string Kind,
    JsonElement Content,
    string AppearanceCss = ""
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

    internal static FullOverlayWidgetFrame Source(
        Guid widgetId,
        FullOverlayAudio audio,
        OverlaySnapshotProjection projection
    )
    {
        return projection switch
        {
            OverlaySnapshotProjection.EmptyV1 value => Frame(value.Snapshot),
            OverlaySnapshotProjection.GuessingV1 value => Frame(
                value.Snapshot,
                value.Snapshot.Appearance
            ),
            OverlaySnapshotProjection.CuePlayerV1 value => Frame(value.Snapshot),
            OverlaySnapshotProjection.GiveawayV1 value => Frame(
                value.Snapshot,
                value.Snapshot.Appearance
            ),
            OverlaySnapshotProjection.EventFeedV1 value => Frame(
                value.Snapshot,
                value.Snapshot.Appearance
            ),
            OverlaySnapshotProjection.ViewerQueueV1 value => Frame(
                value.Snapshot,
                value.Snapshot.Appearance
            ),
            OverlaySnapshotProjection.CommunityGoalV1 value => Frame(
                value.Snapshot,
                value.Snapshot.Appearance
            ),
            OverlaySnapshotProjection.ViewerFundedBountyV1 value => Frame(
                value.Snapshot,
                value.Snapshot.Appearance
            ),
            OverlaySnapshotProjection.Unavailable => Frame(new { unavailable = true }),
            _ => throw new InvalidOperationException("Unknown overlay snapshot projection."),
        };

        FullOverlayWidgetFrame Frame<T>(T snapshot, OverlayAppearance? appearance = null) =>
            new(
                widgetId,
                audio,
                "source",
                Json(snapshot),
                appearance?.ToScopedCss($"#blokebot-native-{widgetId}") ?? ""
            );
    }
}
