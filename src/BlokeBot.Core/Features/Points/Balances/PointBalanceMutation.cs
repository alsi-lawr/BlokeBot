namespace BlokeBot.Core.Features.Points.Balances;

public sealed record PointBalanceMutation(PointAmount Balance, PointAmount Amount);

public abstract record PointBalanceMutationFailure
{
    private PointBalanceMutationFailure() { }

    public abstract TResult Match<TResult>(
        Func<InvalidAmount, TResult> invalidAmount,
        Func<UnknownUser, TResult> unknownUser,
        Func<InsufficientBalance, TResult> insufficientBalance,
        Func<CapExceeded, TResult> capExceeded
    );

    public sealed record InvalidAmount : PointBalanceMutationFailure
    {
        public override TResult Match<TResult>(
            Func<InvalidAmount, TResult> invalidAmount,
            Func<UnknownUser, TResult> unknownUser,
            Func<InsufficientBalance, TResult> insufficientBalance,
            Func<CapExceeded, TResult> capExceeded
        )
        {
            return invalidAmount(this);
        }
    }

    public sealed record UnknownUser : PointBalanceMutationFailure
    {
        public override TResult Match<TResult>(
            Func<InvalidAmount, TResult> invalidAmount,
            Func<UnknownUser, TResult> unknownUser,
            Func<InsufficientBalance, TResult> insufficientBalance,
            Func<CapExceeded, TResult> capExceeded
        )
        {
            return unknownUser(this);
        }
    }

    public sealed record InsufficientBalance(PointAmount Balance, PointAmount Amount)
        : PointBalanceMutationFailure
    {
        public override TResult Match<TResult>(
            Func<InvalidAmount, TResult> invalidAmount,
            Func<UnknownUser, TResult> unknownUser,
            Func<InsufficientBalance, TResult> insufficientBalance,
            Func<CapExceeded, TResult> capExceeded
        )
        {
            return insufficientBalance(this);
        }
    }

    public sealed record CapExceeded(PointAmount Balance, PointAmount Amount)
        : PointBalanceMutationFailure
    {
        public override TResult Match<TResult>(
            Func<InvalidAmount, TResult> invalidAmount,
            Func<UnknownUser, TResult> unknownUser,
            Func<InsufficientBalance, TResult> insufficientBalance,
            Func<CapExceeded, TResult> capExceeded
        )
        {
            return capExceeded(this);
        }
    }
}
