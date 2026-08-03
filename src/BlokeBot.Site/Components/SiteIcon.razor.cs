using Microsoft.AspNetCore.Components;

namespace BlokeBot.Site.Components;

public partial class SiteIcon
{
    [Parameter, EditorRequired]
    public required string Name { get; set; }

    [Parameter]
    public string? Class { get; set; }
}
