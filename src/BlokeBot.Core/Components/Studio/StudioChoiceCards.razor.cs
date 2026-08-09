using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Components.Studio;

/// <summary>
/// The focusable group container for <see cref="StudioChoiceCard"/>s: a three-across grid carrying
/// the group role and label. Splatted attributes carry the id and any validation wiring such as
/// <c>aria-invalid</c> and <c>aria-describedby</c>.
/// </summary>
public partial class StudioChoiceCards
{
    [Parameter, EditorRequired]
    public required string AriaLabel { get; set; }

    /// <summary>
    /// Receives the container's element reference during ref capture — before any
    /// <c>OnAfterRender</c> runs — so a page keeping a control dictionary for validation focus can
    /// register the group without owning its markup.
    /// </summary>
    [Parameter]
    public Action<ElementReference>? CaptureElement { get; set; }

    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    [Parameter(CaptureUnmatchedValues = true)]
    public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }

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
