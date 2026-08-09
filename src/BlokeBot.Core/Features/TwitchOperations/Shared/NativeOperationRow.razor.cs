using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.TwitchOperations.Shared;

/// <summary>
/// One bordered row of a native Twitch dashboard: a bold title with muted detail prose beneath,
/// and an optional right-aligned timestamp and action cluster. The five dashboards share this
/// shape for templates, history entries, attempts, redemptions and rewards.
/// </summary>
public partial class NativeOperationRow
{
    [Parameter, EditorRequired]
    public required string Title { get; set; }

    [Parameter]
    public string? When { get; set; }

    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    [Parameter]
    public RenderFragment? Actions { get; set; }

    [Parameter(CaptureUnmatchedValues = true)]
    public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }
}
