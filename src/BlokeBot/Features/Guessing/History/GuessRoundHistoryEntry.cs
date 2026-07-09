namespace BlokeBot.Features.Guessing.History;

public sealed record GuessRoundHistoryEntry(
    int Id,
    string ProfileName,
    DateTime StartedAtUtc,
    DateTime? ClosedAtUtc,
    string? WinningName,
    int GuessCount,
    int CorrectGuessCount
);
