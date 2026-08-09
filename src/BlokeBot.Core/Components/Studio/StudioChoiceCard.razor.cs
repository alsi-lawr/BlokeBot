using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Components.Studio;

/// <summary>
/// One prose-described option of a single-select: a pressed-state button with a bold title and a
/// muted description, replacing a <c>&lt;select&gt;</c> whose enum labels a non-technical viewer
/// cannot read. The check-mark rides the pressed title. <see cref="Icon"/> is optional because
/// half the consuming surfaces lead their cards with an emoji and half do not.
/// </summary>
public partial class StudioChoiceCard
{
    [Parameter, EditorRequired]
    public required string Title { get; set; }

    [Parameter, EditorRequired]
    public required string Description { get; set; }

    [Parameter]
    public string? Icon { get; set; }

    [Parameter]
    public bool Pressed { get; set; }

    [Parameter]
    public EventCallback Select { get; set; }

    [Parameter(CaptureUnmatchedValues = true)]
    public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }
}
