using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Components.Studio;

/// <summary>
/// A compact chip. Supplying <see cref="Pressed"/> makes it a two-state toggle; otherwise it is a
/// static chip that gains a remove affordance when <see cref="Remove"/> is bound.
/// </summary>
public partial class StudioChip
{
    [Parameter, EditorRequired]
    public required string Label { get; set; }

    [Parameter]
    public string? Id { get; set; }

    [Parameter]
    public string? Note { get; set; }

    [Parameter]
    public bool? Pressed { get; set; }

    [Parameter]
    public EventCallback<bool> PressedChanged { get; set; }

    [Parameter]
    public string? PressedNote { get; set; } = "Allowed";

    /// <summary>
    /// Shown beside an unpressed chip. Selections whose unpressed state needs no words clear it.
    /// </summary>
    [Parameter]
    public string? ReleasedNote { get; set; } = "Not allowed";

    [Parameter]
    public EventCallback Remove { get; set; }

    [Parameter]
    public string? RemoveLabel { get; set; }

    [Parameter]
    public string? RemoveAction { get; set; }

    [Parameter(CaptureUnmatchedValues = true)]
    public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }

    private Task Toggle() => PressedChanged.InvokeAsync(Pressed != true);
}
