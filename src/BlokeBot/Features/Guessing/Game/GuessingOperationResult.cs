namespace BlokeBot.Features.Guessing.Game;

public sealed record GuessingOperationResult(
    bool Succeeded,
    string Message,
    CommandResponseTarget Target = CommandResponseTarget.Chat
);
