namespace BlokeBot.Core.Features.Guessing.Game;

public abstract record GuessingOperationOutcome
{
    private GuessingOperationOutcome() { }

    public abstract string Message { get; init; }

    public abstract CommandResponseTarget Target { get; init; }

    public virtual PublicChatPinIntent? Pin => null;

    public sealed record Succeeded(
        string Message,
        CommandResponseTarget Target = CommandResponseTarget.Chat,
        PublicChatPinIntent? PublicChatPin = null
    ) : GuessingOperationOutcome
    {
        public override PublicChatPinIntent? Pin => PublicChatPin;
    }

    public sealed record Rejected(
        string Message,
        CommandResponseTarget Target = CommandResponseTarget.Chat
    ) : GuessingOperationOutcome;
}
