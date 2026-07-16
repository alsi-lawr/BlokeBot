using BlokeBot.Core.Features.Points.Balances;

namespace BlokeBot.Core.Features.Guessing.Game;

public abstract record GuessingWinnerDeclarationOutcome
{
    private GuessingWinnerDeclarationOutcome() { }

    public abstract TResult Match<TResult>(
        Func<Completed, TResult> completed,
        Func<PayoutFailed, TResult> payoutFailed
    );

    public sealed record Completed(GuessingOperationOutcome Result)
        : GuessingWinnerDeclarationOutcome
    {
        public override TResult Match<TResult>(
            Func<Completed, TResult> completed,
            Func<PayoutFailed, TResult> payoutFailed
        )
        {
            return completed(this);
        }
    }

    public sealed record PayoutFailed(PointBalanceMutationFailure Failure)
        : GuessingWinnerDeclarationOutcome
    {
        public string Message => "Winner rewards could not be awarded.";

        public CommandResponseTarget Target => CommandResponseTarget.Chat;

        public override TResult Match<TResult>(
            Func<Completed, TResult> completed,
            Func<PayoutFailed, TResult> payoutFailed
        )
        {
            return payoutFailed(this);
        }
    }
}
