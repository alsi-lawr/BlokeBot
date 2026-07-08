namespace BlokeBot.Features.Guessing.Rounds;

public sealed record GuessRoundView(
    int Id,
    int ProfileId,
    string ProfileName,
    GuessRoundStatus Status,
    DateTime StartedAtUtc,
    DateTime? ClosedAtUtc,
    string? WinningName
);
