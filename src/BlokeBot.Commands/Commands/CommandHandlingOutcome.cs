using System.Diagnostics;

namespace BlokeBot.Commands;

public abstract record CommandHandlingOutcome
{
    private CommandHandlingOutcome() { }

    public TResult Match<TResult>(
        Func<Unhandled, TResult> unhandled,
        Func<Handled, TResult> handled
    )
    {
        return this switch
        {
            Unhandled value => unhandled(value),
            Handled value => handled(value),
            _ => throw new UnreachableException("Unknown command handling outcome."),
        };
    }

    public sealed record Unhandled : CommandHandlingOutcome;

    public sealed record Handled : CommandHandlingOutcome;
}
