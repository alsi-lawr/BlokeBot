using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.Home;

public partial class HomeInfoCard
{
    [Parameter, EditorRequired]
    public string Title { get; set; } = string.Empty;

    [Parameter, EditorRequired]
    public HomeInfoCardIcon Icon { get; set; }

    [Parameter, EditorRequired]
    public string Href { get; set; } = string.Empty;

    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    private string _cardClass => $"home-info-card home-info-card--{_iconCssClass} rounded-lg p-5";

    private string _iconCssClass =>
        Icon switch
        {
            HomeInfoCardIcon.Channel => "channel",
            HomeInfoCardIcon.Points => "points",
            HomeInfoCardIcon.Moderators => "moderators",
            HomeInfoCardIcon.Bot => "bot",
            _ => "channel",
        };
}

public enum HomeInfoCardIcon
{
    Channel,
    Points,
    Moderators,
    Bot,
}
