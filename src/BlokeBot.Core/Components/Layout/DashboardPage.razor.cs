using System.Diagnostics;
using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Components.Layout;

public partial class DashboardPage
{
    [Parameter]
    public RenderFragment? Actions { get; set; }

    [Parameter(CaptureUnmatchedValues = true)]
    public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }

    [Parameter, EditorRequired]
    public RenderFragment? ChildContent { get; set; }

    [Parameter]
    public string Description { get; set; } = string.Empty;

    [Parameter]
    public string Kicker { get; set; } = string.Empty;

    [Parameter]
    public PageLoadState LoadState { get; set; } = new PageLoadState.Ready();

    [Parameter]
    public PageSaveFeedback? SaveFeedback { get; set; }

    [Parameter]
    public RenderFragment? SaveAction { get; set; }

    [Parameter, EditorRequired]
    public string Title { get; set; } = string.Empty;

    [Parameter]
    public DashboardPageWidth Width { get; set; } = DashboardPageWidth.Readable;

    private string _pageClass =>
        Width switch
        {
            DashboardPageWidth.Readable =>
                "dashboard-page dashboard-page--readable app-motion-stack",
            DashboardPageWidth.Wide => "dashboard-page dashboard-page--wide app-motion-stack",
            _ => throw new UnreachableException(),
        };
}
