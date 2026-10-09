namespace BlokeBot.Core.Features.Overlays.Full;

public sealed record FullOverlayEditorLayer(
    string Key,
    string? Parent,
    string Label,
    string Tag,
    string Selector,
    string? WidgetId
);

public enum FullOverlaySelectionMode
{
    Replace,
    Toggle,
}

public sealed record FullOverlayLayerSelection(string Key, FullOverlaySelectionMode Mode);

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
    public IReadOnlyList<string> Members { get; init; } = [];
    public long SelectionVersion { get; init; }
    public string HierarchyRoot { get; init; } = "Overlay root";
    public IReadOnlyDictionary<string, string> NativeStyles { get; init; } =
        new Dictionary<string, string>();

    internal static FullOverlayEditorView Empty { get; } =
        new(0, false, null, [], null, false, new Dictionary<string, string>(), "", "visual", []);
}

public sealed record FullOverlayHierarchyFeedback(bool Valid, string Message);
