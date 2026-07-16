using BlokeBot.Core.Features.Guessing.Rounds;

namespace BlokeBot.Core.Features.Guessing.History;

public sealed record GuessRoundHistoryEntry(
    int Id,
    string ProfileName,
    GuessRoundLifecycle.Completed Lifecycle,
    int GuessCount,
    int CorrectGuessCount
);
