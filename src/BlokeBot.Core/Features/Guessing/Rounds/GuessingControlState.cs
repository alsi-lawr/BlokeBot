using System.Collections.Immutable;
using BlokeBot.Core.Features.Guessing.Guesses;
using BlokeBot.Core.Features.Guessing.Profiles;

namespace BlokeBot.Core.Features.Guessing.Rounds;

internal sealed record GuessingControlState(
    GuessRoundProfileSummary SelectedProfile,
    GuessRoundView? CurrentRound,
    ImmutableArray<GuessName> WinnerOptions
);
