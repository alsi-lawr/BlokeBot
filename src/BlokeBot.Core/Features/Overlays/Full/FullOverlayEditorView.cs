namespace BlokeBot.Core.Features.Overlays.Full;

public sealed record FullOverlayEditorLayer(
    string Key,
    string? Parent,
    string Label,
    string Tag,
    string Selector,
    string? WidgetId
);

public sealed record FullOverlayEditorSourceDiagnostic(string Code, string Buffer, int Offset);

public sealed record FullOverlayEditorView(
    long Revision,
    bool Dirty,
    string? Selected,
    IReadOnlyList<FullOverlayEditorLayer> Layers,
    FullOverlayWidget? Widget,
    bool Locked,
    IReadOnlyDictionary<string, string> Styles,
    string Feedback,
    string Focus,
    IReadOnlyList<FullOverlayEditorSourceDiagnostic> Diagnostics
)
{
    internal static FullOverlayEditorView Empty { get; } =
        new(0, false, null, [], null, false, new Dictionary<string, string>(), "", "visual", []);
}
