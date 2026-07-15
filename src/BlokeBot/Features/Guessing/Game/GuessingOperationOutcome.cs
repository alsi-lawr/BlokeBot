namespace BlokeBot.Features.Guessing.Game;

public abstract record GuessingOperationOutcome
{
    private GuessingOperationOutcome() { }

    public abstract string Message { get; init; }

    public abstract CommandResponseTarget Target { get; init; }

    public sealed record Succeeded(
        string Message,
        CommandResponseTarget Target = CommandResponseTarget.Chat
    ) : GuessingOperationOutcome;

    public sealed record Rejected(
        string Message,
        CommandResponseTarget Target = CommandResponseTarget.Chat
    ) : GuessingOperationOutcome;
}
