namespace BlokeBot.Core.Features.Bingo;

public sealed record BingoSelfCard(string AssignmentName, int MarkedSquares, int TotalSquares);

public abstract record BingoSelfCardOutcome
{
    private BingoSelfCardOutcome() { }

    public abstract TResult Match<TResult>(
        Func<Available, TResult> available,
        Func<NotJoined, TResult> notJoined,
        Func<FeatureDisabled, TResult> featureDisabled
    );

    public sealed record Available(BingoSelfCard Card) : BingoSelfCardOutcome
    {
        public override TResult Match<TResult>(
            Func<Available, TResult> available,
            Func<NotJoined, TResult> notJoined,
            Func<FeatureDisabled, TResult> featureDisabled
        ) => available(this);
    }

    public sealed record NotJoined : BingoSelfCardOutcome
    {
        public override TResult Match<TResult>(
            Func<Available, TResult> available,
            Func<NotJoined, TResult> notJoined,
            Func<FeatureDisabled, TResult> featureDisabled
        ) => notJoined(this);
    }

    public sealed record FeatureDisabled : BingoSelfCardOutcome
    {
        public override TResult Match<TResult>(
            Func<Available, TResult> available,
            Func<NotJoined, TResult> notJoined,
            Func<FeatureDisabled, TResult> featureDisabled
        ) => featureDisabled(this);
    }
}
