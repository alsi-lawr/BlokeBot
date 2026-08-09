using System.Diagnostics;
using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Components.Studio;

/// <summary>
/// The focusable group container for <see cref="StudioChoiceCard"/>s: a grid carrying the group
/// role and label, single-column on phones and <see cref="Columns"/>-across above. Splatted
/// attributes carry the id and any validation wiring such as <c>aria-invalid</c> and
/// <c>aria-describedby</c>.
/// </summary>
public partial class StudioChoiceCards
{
    [Parameter, EditorRequired]
    public required string AriaLabel { get; set; }

    [Parameter]
    public StudioChoiceCardsColumns Columns { get; set; } = StudioChoiceCardsColumns.Three;

    /// <summary>
    /// Receives the container's element reference during ref capture, before any
    /// <c>OnAfterRender</c> runs, so a page keeping a control dictionary for validation focus can
    /// register the group without owning its markup.
    /// </summary>
    [Parameter]
    public Action<ElementReference>? CaptureElement { get; set; }

    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    [Parameter(CaptureUnmatchedValues = true)]
    public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }

    // Complete literal class strings only: Tailwind emits a utility only when it can read the
    // whole class in source, so a composed $"md:grid-cols-{n}" is never generated and the group
    // silently renders single-column.
    private string _columnClasses =>
        Columns switch
        {
            StudioChoiceCardsColumns.Three => "md:grid-cols-3",
            StudioChoiceCardsColumns.Two => "md:grid-cols-2",
            StudioChoiceCardsColumns.TwoThenThree => "md:grid-cols-2 xl:grid-cols-3",
            _ => throw new UnreachableException(),
        };

    private ElementReference _element
    {
        get;
        set
        {
            field = value;
            CaptureElement?.Invoke(value);
        }
    }
}

/// <summary>
/// Column count above the single-column phone layout.
/// </summary>
public enum StudioChoiceCardsColumns
{
    /// <summary>Three across from <c>md</c>.</summary>
    Three,

    /// <summary>Two across from <c>md</c>, for a two-card group.</summary>
    Two,

    /// <summary>
    /// Two across from <c>md</c>, opening to three from <c>xl</c>, for a three-card group whose
    /// container is too narrow below <c>xl</c> to set three columns of bold prose.
    /// </summary>
    TwoThenThree,
}
