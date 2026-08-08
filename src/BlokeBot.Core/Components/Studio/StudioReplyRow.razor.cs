using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Components.Studio;

/// <summary>
/// One bot message in a narrated reply list: its name, an excerpt of what it currently says, and
/// the state it is in, disclosing the editor on click. A row whose message can only reach chat
/// simply carries no delivery control in its body.
/// </summary>
public partial class StudioReplyRow
{
    [Parameter, EditorRequired]
    public required string Key { get; set; }

    [Parameter, EditorRequired]
    public required string Label { get; set; }

    [Parameter]
    public string? Excerpt { get; set; }

    [Parameter]
    public bool Customised { get; set; }

    [Parameter]
    public bool Whispered { get; set; }

    [Parameter]
    public bool Open { get; set; }

    [Parameter]
    public EventCallback<bool> OpenChanged { get; set; }

    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    public static string BodyId(string key) => $"studio-reply-{key}";

    private string _bodyId => BodyId(Key);

    private string _state =>
        Whispered ? "whisper"
        : Customised ? "custom"
        : "default";

    private Task Toggle() => OpenChanged.InvokeAsync(!Open);
}
