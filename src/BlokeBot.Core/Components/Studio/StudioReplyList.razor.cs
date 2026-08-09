using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Components.Studio;

/// <summary>
/// A run of <see cref="StudioReplyRow"/> entries. <see cref="Label"/> names and heads one group, so
/// a surface whose replies fall into named groups writes one list per group; a surface whose
/// replies are one flat run leaves the label off and gets exactly that.
/// </summary>
public partial class StudioReplyList
{
    [Parameter]
    public string? Label { get; set; }

    [Parameter]
    public RenderFragment? ChildContent { get; set; }
}
