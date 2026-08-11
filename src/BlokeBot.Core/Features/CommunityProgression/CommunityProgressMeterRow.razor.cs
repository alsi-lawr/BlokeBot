using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.CommunityProgression;

/// <summary>
/// One quest, achievement, or communal goal as a labelled progress meter.
/// </summary>
public partial class CommunityProgressMeterRow
{
    [Parameter, EditorRequired]
    public required string Name { get; set; }

    [Parameter]
    public string? Tag { get; set; }

    [Parameter, EditorRequired]
    public required long Amount { get; set; }

    [Parameter, EditorRequired]
    public required long Target { get; set; }

    [Parameter]
    public int CompletionCount { get; set; }

    [Parameter]
    public string? Note { get; set; }

    private string _amount => CommunityProgressionPresentation.Amount(Amount);

    private string _target => CommunityProgressionPresentation.Amount(Target);

    private string _completions =>
        CompletionCount > 0 ? $" · {CompletionCount} completed" : string.Empty;

    private string _meterClass => CommunityProgressionPresentation.MeterClass(Amount, Target);

    private string _meterLabel => $"{Name} progress";

    private int _meterPercent => CommunityProgressionPresentation.MeterPercent(Amount, Target);

    private string _fillStyle => CommunityProgressionPresentation.MeterFillStyle(Amount, Target);
}
