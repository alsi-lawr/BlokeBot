using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Components.Studio;

/// <summary>
/// One tile in a thumbnail grid: an aspect-boxed art area above a name and a sub-line. Supplying
/// <see cref="Selected"/> makes the whole tile a pressable choice driving a panel elsewhere;
/// omitting it leaves a static tile whose own actions go in <see cref="Actions"/>. The art area is
/// only a positioned box, so a caller overlays a pill, a badge or both by placing them itself. The
/// enclosing grid stays at the call site, because the surfaces that need this disagree about their
/// column counts and Tailwind only generates utilities it can read literally in source.
/// </summary>
public partial class StudioThumbnailCard
{
    /// <summary>
    /// Chrome common to both shapes. Colours come from the palette variables rather than fixed
    /// slate utilities so the tile follows the theme without a dark-mode shim.
    /// </summary>
    private const string _tileClass =
        "block w-full overflow-hidden rounded-xl border border-[var(--app-control-border)] "
        + "bg-[var(--app-control-bg)] text-left transition duration-150 hover:-translate-y-0.5 "
        + "hover:border-[var(--app-control-hover-border)] hover:shadow-[var(--app-shadow-sm)] "
        + "motion-reduce:transition-none motion-reduce:hover:translate-y-0";

    /// <summary>
    /// A pressable tile additionally fills on hover and carries the app's border-plus-shadow
    /// selection idiom.
    /// </summary>
    private const string _selectableClass =
        _tileClass
        + " hover:bg-[var(--app-control-hover)] focus-visible:border-[var(--app-focus-border)] "
        + "focus-visible:shadow-[var(--app-focus-shadow)] focus-visible:outline-none "
        + "aria-pressed:border-[var(--app-focus-border)] aria-pressed:bg-[var(--app-surface-solid)] "
        + "aria-pressed:shadow-[var(--app-shadow-sm)]";

    [Parameter, EditorRequired]
    public required string Name { get; set; }

    [Parameter]
    public string? Subline { get; set; }

    [Parameter]
    public RenderFragment? Thumbnail { get; set; }

    /// <summary>
    /// The art area's own utilities, including its aspect ratio, so a surface picks both its
    /// proportions and its per-item treatment without this component knowing either.
    /// </summary>
    [Parameter]
    public string ThumbnailClass { get; set; } = "aspect-video";

    [Parameter]
    public RenderFragment? Actions { get; set; }

    /// <summary>
    /// Present makes the tile a two-state choice; absent leaves it inert display.
    /// </summary>
    [Parameter]
    public bool? Selected { get; set; }

    [Parameter]
    public EventCallback Select { get; set; }

    private Task OnSelect() => Select.InvokeAsync();
}
