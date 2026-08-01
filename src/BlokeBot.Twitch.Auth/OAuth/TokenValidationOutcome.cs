namespace BlokeBot.Twitch.Auth;

public abstract record TokenValidationOutcome
{
    private TokenValidationOutcome() { }

    public abstract TResult Match<TResult>(
        Func<Validated, TResult> validated,
        Func<NotValidated, TResult> notValidated
    );

    public sealed record Validated(TokenValidation Validation) : TokenValidationOutcome
    {
        public override TResult Match<TResult>(
            Func<Validated, TResult> validated,
            Func<NotValidated, TResult> notValidated
        ) => validated(this);
    }

    public sealed record NotValidated : TokenValidationOutcome
    {
        public override TResult Match<TResult>(
            Func<Validated, TResult> validated,
            Func<NotValidated, TResult> notValidated
        ) => notValidated(this);
    }
}
