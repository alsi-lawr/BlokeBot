using Microsoft.AspNetCore.Components;

namespace BlokeBot.Site.Components;

public partial class GuideVideo
{
    [Parameter, EditorRequired]
    public required string Title { get; set; }

    [Parameter, EditorRequired]
    public required string Description { get; set; }

    [Parameter, EditorRequired]
    public required string LightSource { get; set; }

    [Parameter, EditorRequired]
    public required string DarkSource { get; set; }

    [Parameter, EditorRequired]
    public required string LightStill { get; set; }

    [Parameter, EditorRequired]
    public required string DarkStill { get; set; }

    [Parameter]
    public bool Phone { get; set; }
}
