using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Components.Layout;

public partial class PageHeader
{
    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    [Parameter]
    public string Description { get; set; } = string.Empty;

    [Parameter]
    public string Kicker { get; set; } = string.Empty;

    [Parameter]
    public string SaveStatus { get; set; } = string.Empty;

    [Parameter, EditorRequired]
    public string Title { get; set; } = string.Empty;
}
