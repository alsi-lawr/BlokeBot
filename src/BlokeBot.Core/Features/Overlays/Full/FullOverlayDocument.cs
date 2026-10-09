using System.Collections.Immutable;
using System.Text.Json;

namespace BlokeBot.Core.Features.Overlays.Full;

public readonly record struct FullOverlayWidgetKind(string Value);

public readonly record struct FullOverlayWidgetId(Guid Value);

// Html/Css are the authored document. Metadata supports authoring; it does not regenerate source
// or override authored layout during rendering. Visual edits must update the same source explicitly.
public sealed record FullOverlayDocument(
    Guid Id,
    string Html,
    string Css,
    ImmutableArray<FullOverlayWidget> Widgets,
    ImmutableArray<FullOverlayDiagnostic> Diagnostics
)
{
    public static FullOverlayDocument Blank() => new(Guid.NewGuid(), "", "", [], []);
}

public sealed record FullOverlayWidget(
    FullOverlayWidgetId Id,
    FullOverlayWidgetKind Kind,
    JsonElement Configuration,
    FullOverlayAuthoringMetadata Authoring,
    FullOverlayAudio Audio
);

public sealed record FullOverlayAudio(bool IsMuted, double Volume)
{
    public static FullOverlayAudio Default { get; } = new(false, 1);
}

public enum FullOverlayAnchor
{
    Start,
    Centre,
    End,
}

public sealed record FullOverlayAuthoringMetadata(
    bool IsVisible,
    bool IsLocked,
    bool ClipOverflow,
    string X,
    string Y,
    string Width,
    string Height,
    double RotationDegrees,
    double ScaleX,
    double ScaleY,
    FullOverlayAnchor HorizontalAnchor,
    FullOverlayAnchor VerticalAnchor
)
{
    public static FullOverlayAuthoringMetadata Default { get; } =
        new(
            true,
            false,
            false,
            "0px",
            "0px",
            "auto",
            "auto",
            0,
            1,
            1,
            FullOverlayAnchor.Start,
            FullOverlayAnchor.Start
        );
}

public enum FullOverlayDiagnosticSeverity
{
    Information,
    Warning,
    Error,
}

public sealed record FullOverlayDiagnostic(
    string Code,
    string Message,
    FullOverlayDiagnosticSeverity Severity,
    FullOverlayWidgetId? WidgetId = null
);

internal static class FullOverlayDocuments
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    internal static string Serialize(FullOverlayDocument document) =>
        JsonSerializer.Serialize(document, _json);

    internal static FullOverlayDocument Deserialize(string json) =>
        JsonSerializer.Deserialize<FullOverlayDocument>(json, _json)!;

    internal static bool HasCoherentIdentity(FullOverlayDocument document) =>
        document.Id != Guid.Empty
        && document.Html is not null
        && document.Css is not null
        && !document.Widgets.IsDefault
        && !document.Diagnostics.IsDefault
        && document.Widgets.All(widget =>
            widget is not null
            && widget.Id.Value != Guid.Empty
            && widget.Configuration.ValueKind != JsonValueKind.Undefined
            && widget.Authoring is not null
            && widget.Audio is not null
        )
        && document.Widgets.Select(widget => widget.Id).Distinct().Count()
            == document.Widgets.Length;
}
