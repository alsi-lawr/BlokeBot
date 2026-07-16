using System.Collections.Immutable;
using BlokeBot.Core.Features.Guessing.Guesses;
using BlokeBot.Core.Features.Guessing.Profiles;

namespace BlokeBot.Core.Features.Guessing.Rounds;

public sealed record GuessingDashboardState
{
    public GuessRoundView? CurrentRound { get; init; }
    public ImmutableArray<GuessVoteView> Votes { get; init; } = [];
    public IReadOnlyList<GuessOptionEditor> Options { get; init; } = [];
    public ImmutableArray<GuessRoundProfileSummary> Profiles { get; init; } = [];
}
