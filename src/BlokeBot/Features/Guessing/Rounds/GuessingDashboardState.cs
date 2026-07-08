using BlokeBot.Features.Guessing.Guesses;
using BlokeBot.Features.Guessing.Profiles;

namespace BlokeBot.Features.Guessing.Rounds;

public sealed record GuessingDashboardState
{
    public GuessRoundView? CurrentRound { get; init; }
    public IReadOnlyList<GuessVoteView> Votes { get; init; } = [];
    public IReadOnlyList<GuessOptionEditor> Options { get; init; } = [];
    public IReadOnlyList<GuessRoundProfileSummary> Profiles { get; init; } = [];
}
