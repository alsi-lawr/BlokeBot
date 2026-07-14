using System.Collections.Immutable;
using BlokeBot.Features.Guessing.Guesses;
using BlokeBot.Features.Guessing.Profiles;

namespace BlokeBot.Features.Guessing.Rounds;

public sealed record GuessingDashboardState
{
    public GuessRoundView? CurrentRound { get; init; }
    public ImmutableArray<GuessVoteView> Votes { get; init; } = [];
    public IReadOnlyList<GuessOptionEditor> Options { get; init; } = [];
    public ImmutableArray<GuessRoundProfileSummary> Profiles { get; init; } = [];
}
