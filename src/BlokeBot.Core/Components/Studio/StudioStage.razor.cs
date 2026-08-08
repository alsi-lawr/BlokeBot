using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Components.Studio;

/// <summary>
/// One numbered stage of a staged-disclosure editor. The closed header narrates the stage's current
/// state through <see cref="Summary"/>, so the whole configuration reads without opening anything.
/// </summary>
public partial class StudioStage
{
    [Parameter, EditorRequired]
    public required string Key { get; set; }

    [Parameter, EditorRequired]
    public required int Number { get; set; }

    [Parameter, EditorRequired]
    public required string Title { get; set; }

    [Parameter]
    public string? Summary { get; set; }

    [Parameter]
    public bool Open { get; set; }

    [Parameter]
    public EventCallback<bool> OpenChanged { get; set; }

    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    public static string BodyId(string key) => $"studio-stage-{key}";

    private string _bodyId => BodyId(Key);

    private Task Toggle() => OpenChanged.InvokeAsync(!Open);
}
