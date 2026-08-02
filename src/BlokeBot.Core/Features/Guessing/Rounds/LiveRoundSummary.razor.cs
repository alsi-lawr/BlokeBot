using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.Guessing.Rounds;

public partial class LiveRoundSummary
{
    private string _winnerName =>
        State.CurrentRound?.Lifecycle.Match(
            static _ => "None",
            static _ => "None",
            static completed => completed.WinningName
        ) ?? "None";

    [Parameter, EditorRequired]
    public string RoundStartedText { get; set; } = string.Empty;

    [Parameter, EditorRequired]
    public string RoundStatusText { get; set; } = string.Empty;

    [Parameter, EditorRequired]
    public GuessingDashboardState State { get; set; } = new();
}
