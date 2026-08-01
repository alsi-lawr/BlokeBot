namespace BlokeBot.Core.Features.Points.Balances;

public abstract record PointGambleOutcome
{
    private PointGambleOutcome() { }

    public abstract TResult Match<TResult>(Func<Won, TResult> won, Func<Lost, TResult> lost);

    public sealed record Won : PointGambleOutcome
    {
        public override TResult Match<TResult>(Func<Won, TResult> won, Func<Lost, TResult> lost) =>
            won(this);
    }

    public sealed record Lost : PointGambleOutcome
    {
        public override TResult Match<TResult>(Func<Won, TResult> won, Func<Lost, TResult> lost) =>
            lost(this);
    }
}
