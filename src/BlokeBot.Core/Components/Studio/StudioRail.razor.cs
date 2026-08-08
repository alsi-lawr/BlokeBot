using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Components.Studio;

/// <summary>
/// One selectable entry in a <see cref="StudioRail"/>. <c>Search</c> defaults to the label, so
/// entries whose searchable text is wider than their label — command aliases, for example — supply
/// it explicitly.
/// </summary>
public sealed record StudioRailItem
{
    public required string Key { get; init; }

    public required string Label { get; init; }

    public required EventCallback Select { get; init; }

    public string? Search { get; init; }

    public string? LabelId { get; init; }

    public string? ControlsId { get; init; }

    public string? Action { get; init; }

    public string? Meta { get; init; }

    /// <summary>
    /// A muted second line under the label, for entries whose state needs prose the one-line
    /// label-plus-meta form cannot carry.
    /// </summary>
    public string? Sub { get; init; }

    public bool Monospace { get; init; }

    public bool On { get; init; }

    public bool Selected { get; init; }
}

public sealed record StudioRailAdd(
    string Action,
    string Label,
    EventCallback Invoke,
    bool Disabled = false
);

public sealed record StudioRailGroup(
    string Label,
    IReadOnlyList<StudioRailItem> Items,
    string? EmptyMessage = null,
    StudioRailAdd? Add = null
);

public partial class StudioRail
{
    [Parameter, EditorRequired]
    public required string AriaLabel { get; set; }

    [Parameter, EditorRequired]
    public required IReadOnlyList<StudioRailGroup> Groups { get; set; }

    /// <summary>
    /// The visible placeholder only. The search box takes its accessible name from
    /// <see cref="AriaLabel"/>, which names the whole rail and so stays legible when the rail is
    /// too narrow to show a long placeholder.
    /// </summary>
    [Parameter]
    public string SearchPlaceholder { get; set; } = "Search";

    [Parameter]
    public string NoMatchMessage { get; set; } = "Nothing matches your search.";

    [Parameter]
    public RenderFragment? Footer { get; set; }

    private string _search = string.Empty;

    private IEnumerable<StudioRailItem> Matching(StudioRailGroup group) =>
        _search.Trim() is { Length: > 0 } term
            ? group.Items.Where(item =>
                (item.Search ?? item.Label).Contains(term, StringComparison.OrdinalIgnoreCase)
            )
            : group.Items;

    private string EmptyLabel(StudioRailGroup group) =>
        group.Items.Count == 0 ? group.EmptyMessage ?? NoMatchMessage : NoMatchMessage;
}
