using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Components.Studio;

public sealed record StudioSegmentedOption<TValue>(TValue Value, string Label, string? Id = null);

/// <summary>
/// The rounded pill segmented control: a small set of mutually exclusive choices pressed in place.
/// Unlike <c>SegmentedTabs</c> this is a control over a value rather than a tab strip over panels,
/// so it carries no roving focus, no panel ownership, and no URL fragment.
/// </summary>
public partial class StudioSegmentedPill<TValue>
{
    [Parameter, EditorRequired]
    public required string AriaLabel { get; set; }

    [Parameter, EditorRequired]
    public required IReadOnlyList<StudioSegmentedOption<TValue>> Options { get; set; }

    [Parameter]
    public TValue? Value { get; set; }

    [Parameter]
    public EventCallback<TValue> ValueChanged { get; set; }

    [Parameter]
    public bool Disabled { get; set; }

    /// <summary>
    /// Points the disabled options at the recovery text explaining what would re-enable them.
    /// </summary>
    [Parameter]
    public string? DisabledDescriptionId { get; set; }

    private bool IsSelected(StudioSegmentedOption<TValue> option) =>
        EqualityComparer<TValue?>.Default.Equals(option.Value, Value);
}
