namespace BlokeBot.Commands;

public abstract record CommandHandlingOutcome
{
    private CommandHandlingOutcome() { }

    public abstract TResult Match<TResult>(
        Func<Unhandled, TResult> unhandled,
        Func<Handled, TResult> handled
    );

    public sealed record Unhandled : CommandHandlingOutcome
    {
        public override TResult Match<TResult>(
            Func<Unhandled, TResult> unhandled,
            Func<Handled, TResult> handled
        )
        {
            return unhandled(this);
        }
    }

    public sealed record Handled : CommandHandlingOutcome
    {
        public override TResult Match<TResult>(
            Func<Unhandled, TResult> unhandled,
            Func<Handled, TResult> handled
        )
        {
            return handled(this);
        }
    }
}
