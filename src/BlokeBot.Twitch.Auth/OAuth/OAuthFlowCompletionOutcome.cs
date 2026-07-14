namespace BlokeBot.Twitch.Auth;

public abstract record OAuthFlowCompletionOutcome
{
    private OAuthFlowCompletionOutcome() { }

    public abstract TResult Match<TResult>(
        Func<Completed, TResult> completed,
        Func<InvalidState, TResult> invalidState
    );

    public sealed record Completed(TokenSet Token) : OAuthFlowCompletionOutcome
    {
        public override TResult Match<TResult>(
            Func<Completed, TResult> completed,
            Func<InvalidState, TResult> invalidState
        )
        {
            return completed(this);
        }
    }

    public sealed record InvalidState : OAuthFlowCompletionOutcome
    {
        public override TResult Match<TResult>(
            Func<Completed, TResult> completed,
            Func<InvalidState, TResult> invalidState
        )
        {
            return invalidState(this);
        }
    }
}
