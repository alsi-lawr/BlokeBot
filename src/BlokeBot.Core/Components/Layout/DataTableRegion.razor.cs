using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Components.Layout;

public partial class DataTableRegion
{
    [Parameter, EditorRequired]
    public RenderFragment? ChildContent { get; set; }

    [Parameter, EditorRequired]
    public string Label { get; set; } = string.Empty;
}
