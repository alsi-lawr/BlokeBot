using BlokeBot.Core.Features.Guessing.Guesses;
using Microsoft.AspNetCore.Components;

namespace BlokeBot.Core.Features.Guessing.Rounds;

public partial class GuessVotesTable
{
    [Parameter, EditorRequired]
    public IReadOnlyList<GuessVoteView> Votes { get; set; } = [];
}
