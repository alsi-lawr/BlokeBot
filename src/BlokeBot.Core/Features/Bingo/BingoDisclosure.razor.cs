using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.Bingo;

/// <summary>
/// A card whose header discloses its body, for headers too rich for
/// <see cref="Components.CollapsibleSection"/>: a title beside a status pill, and lifecycle actions
/// that must stay clickable. The title area is the accessible trigger; the chevron repeats it as a
/// pointer target only, so the actions are not nested inside a button.
/// </summary>
public partial class BingoDisclosure
{
    [Parameter, EditorRequired]
    public required string Key { get; set; }

    [Parameter, EditorRequired]
    public required RenderFragment Header { get; set; }

    [Parameter]
    public RenderFragment? Actions { get; set; }

    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    [Parameter]
    public bool Open { get; set; }

    [Parameter]
    public EventCallback<bool> OpenChanged { get; set; }

    [Parameter]
    public string? Class { get; set; }

    [Parameter(CaptureUnmatchedValues = true)]
    public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }

    private string _bodyId => $"bingo-disclosure-{Key}";

    private string _sectionClass =>
        string.IsNullOrWhiteSpace(Class) ? "card min-w-0" : $"card min-w-0 {Class}";

    private Task Toggle() => OpenChanged.InvokeAsync(!Open);
}
