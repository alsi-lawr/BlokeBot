using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Components.Layout;

public partial class TaskPanel
{
    [Parameter]
    public RenderFragment? Actions { get; set; }

    [Parameter, EditorRequired]
    public RenderFragment? ChildContent { get; set; }

    [Parameter]
    public string Description { get; set; } = string.Empty;

    [Parameter]
    public RenderFragment? Footer { get; set; }

    [Parameter, EditorRequired]
    public string Title { get; set; } = string.Empty;
}
