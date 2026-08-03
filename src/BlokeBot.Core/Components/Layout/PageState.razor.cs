using System.Diagnostics;
using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Components.Layout;

public partial class PageState
{
    [Parameter]
    public RenderFragment? Actions { get; set; }

    [Parameter]
    public string Description { get; set; } = string.Empty;

    [Parameter, EditorRequired]
    public PageStateKind Kind { get; set; }

    [Parameter, EditorRequired]
    public string Title { get; set; } = string.Empty;

    private string? _role =>
        Kind switch
        {
            PageStateKind.Loading or PageStateKind.Success => "status",
            PageStateKind.Failure => "alert",
            PageStateKind.Empty or PageStateKind.Unavailable => null,
            _ => throw new UnreachableException(),
        };
}
