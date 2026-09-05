namespace BlokeBot.Core.Features.Bounties;

public abstract record BountyPublicBoardOutcome
{
    private BountyPublicBoardOutcome() { }

    public abstract TResult Match<TResult>(
        Func<Available, TResult> available,
        Func<Disabled, TResult> disabled
    );

    public sealed record Available(IReadOnlyList<BountyView> Bounties) : BountyPublicBoardOutcome
    {
        public override TResult Match<TResult>(
            Func<Available, TResult> available,
            Func<Disabled, TResult> disabled
        ) => available(this);
    }

    public sealed record Disabled : BountyPublicBoardOutcome
    {
        public override TResult Match<TResult>(
            Func<Available, TResult> available,
            Func<Disabled, TResult> disabled
        ) => disabled(this);
    }
}
