using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.Guessing.Rounds;

public partial class RoundOrchestrationPanel
{
    [Parameter, EditorRequired]
    public EventCallback DeclareWinner { get; set; }

    [Parameter, EditorRequired]
    public int SelectedProfileId { get; set; }

    [Parameter]
    public EventCallback<int> SelectedProfileIdChanged { get; set; }

    [Parameter, EditorRequired]
    public EventCallback StartRound { get; set; }

    [Parameter, EditorRequired]
    public GuessingDashboardState State { get; set; } = new();

    [Parameter, EditorRequired]
    public EventCallback StopGuessing { get; set; }

    [Parameter]
    public string WinnerName { get; set; } = string.Empty;

    [Parameter]
    public EventCallback<string> WinnerNameChanged { get; set; }

    /// <summary>
    /// The dashboard projects options as canonical names already, which are the words a viewer
    /// types, so the chips need no further parsing.
    /// </summary>
    private IReadOnlyList<string> _answerNames =>
        State
            .Options.Select(static option => option.Name)
            .Where(static name => name.Length > 0)
            .ToArray();

    private async Task ChooseAnswerAsync(string answer, bool chosen)
    {
        WinnerName = chosen ? answer : string.Empty;
        await WinnerNameChanged.InvokeAsync(WinnerName);
    }

    private async Task OnSelectedProfileChanged(ChangeEventArgs args)
    {
        if (!int.TryParse(args.Value?.ToString(), out var profileId))
        {
            return;
        }

        SelectedProfileId = profileId;
        await SelectedProfileIdChanged.InvokeAsync(profileId);
    }

    private async Task OnWinnerNameChanged(ChangeEventArgs args)
    {
        WinnerName = args.Value?.ToString() ?? string.Empty;
        await WinnerNameChanged.InvokeAsync(WinnerName);
    }

    private async Task InvokeDeclareWinnerAsync() => await DeclareWinner.InvokeAsync();

    private async Task InvokeStartRoundAsync() => await StartRound.InvokeAsync();

    private async Task InvokeStopGuessingAsync() => await StopGuessing.InvokeAsync();
}
