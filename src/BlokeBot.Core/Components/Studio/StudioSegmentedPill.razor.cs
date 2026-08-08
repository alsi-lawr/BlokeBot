using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Components.Studio;

/// <summary>
/// One choice in a <see cref="StudioSegmentedPill{TValue}"/>. <paramref name="Disabled"/> withdraws
/// this choice alone, for a value whose dependency is unmet while its siblings stay selectable.
/// </summary>
public sealed record StudioSegmentedOption<TValue>(
    TValue Value,
    string Label,
    string? Id = null,
    bool Disabled = false
);

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

    private bool IsDisabled(StudioSegmentedOption<TValue> option) => Disabled || option.Disabled;
}
