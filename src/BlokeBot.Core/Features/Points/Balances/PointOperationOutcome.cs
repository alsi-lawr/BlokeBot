namespace BlokeBot.Core.Features.Points.Balances;

public abstract record PointOperationOutcome
{
    private PointOperationOutcome() { }

    public abstract TResult Match<TResult>(
        Func<Succeeded, TResult> succeeded,
        Func<Failed, TResult> failed
    );

    public sealed record Succeeded(string Message, CommandResponseTarget Target)
        : PointOperationOutcome
    {
        public override TResult Match<TResult>(
            Func<Succeeded, TResult> succeeded,
            Func<Failed, TResult> failed
        )
        {
            return succeeded(this);
        }
    }

    public sealed record Failed(string Message, CommandResponseTarget Target)
        : PointOperationOutcome
    {
        public override TResult Match<TResult>(
            Func<Succeeded, TResult> succeeded,
            Func<Failed, TResult> failed
        )
        {
            return failed(this);
        }
    }
}
