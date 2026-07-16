namespace BlokeBot.Core.Features.Guessing.Rounds;

public sealed record GuessRoundView(
    int Id,
    int ProfileId,
    string ProfileName,
    GuessRoundLifecycle Lifecycle
);
