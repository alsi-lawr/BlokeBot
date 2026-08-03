using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Components.Layout;

public partial class ApplicationCardCollection
{
    [Parameter, EditorRequired]
    public RenderFragment? ChildContent { get; set; }

    [Parameter, EditorRequired]
    public string Owner { get; set; } = string.Empty;
}
