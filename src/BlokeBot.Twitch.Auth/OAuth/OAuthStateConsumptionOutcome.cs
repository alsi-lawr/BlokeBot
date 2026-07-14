namespace BlokeBot.Twitch.Auth;

public abstract record OAuthStateConsumptionOutcome
{
    private OAuthStateConsumptionOutcome() { }

    public abstract TResult Match<TResult>(
        Func<Consumed, TResult> consumed,
        Func<Rejected, TResult> rejected
    );

    public sealed record Consumed : OAuthStateConsumptionOutcome
    {
        public override TResult Match<TResult>(
            Func<Consumed, TResult> consumed,
            Func<Rejected, TResult> rejected
        )
        {
            return consumed(this);
        }
    }

    public sealed record Rejected : OAuthStateConsumptionOutcome
    {
        public override TResult Match<TResult>(
            Func<Consumed, TResult> consumed,
            Func<Rejected, TResult> rejected
        )
        {
            return rejected(this);
        }
    }
}
