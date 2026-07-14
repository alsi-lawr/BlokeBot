namespace BlokeBot.Features.HostedChannels.Authorization;

public abstract record OAuthAuthorizationStartOutcome
{
    private OAuthAuthorizationStartOutcome() { }

    public abstract TResult Match<TResult>(
        Func<Ready, TResult> ready,
        Func<ConfigurationUnavailable, TResult> configurationUnavailable
    );

    public sealed record Ready(Uri AuthorizationUri) : OAuthAuthorizationStartOutcome
    {
        public override TResult Match<TResult>(
            Func<Ready, TResult> ready,
            Func<ConfigurationUnavailable, TResult> configurationUnavailable
        )
        {
            return ready(this);
        }
    }

    public sealed record ConfigurationUnavailable : OAuthAuthorizationStartOutcome
    {
        public override TResult Match<TResult>(
            Func<Ready, TResult> ready,
            Func<ConfigurationUnavailable, TResult> configurationUnavailable
        )
        {
            return configurationUnavailable(this);
        }
    }
}

public abstract record OAuthAuthorizationCompletionOutcome<TGrant>
{
    private OAuthAuthorizationCompletionOutcome() { }

    public abstract TResult Match<TResult>(
        Func<Completed, TResult> completed,
        Func<ConfigurationUnavailable, TResult> configurationUnavailable,
        Func<ProviderNotValidated, TResult> providerNotValidated
    );

    public sealed record Completed(TGrant Grant) : OAuthAuthorizationCompletionOutcome<TGrant>
    {
        public override TResult Match<TResult>(
            Func<Completed, TResult> completed,
            Func<ConfigurationUnavailable, TResult> configurationUnavailable,
            Func<ProviderNotValidated, TResult> providerNotValidated
        )
        {
            return completed(this);
        }
    }

    public sealed record ConfigurationUnavailable : OAuthAuthorizationCompletionOutcome<TGrant>
    {
        public override TResult Match<TResult>(
            Func<Completed, TResult> completed,
            Func<ConfigurationUnavailable, TResult> configurationUnavailable,
            Func<ProviderNotValidated, TResult> providerNotValidated
        )
        {
            return configurationUnavailable(this);
        }
    }

    public sealed record ProviderNotValidated : OAuthAuthorizationCompletionOutcome<TGrant>
    {
        public override TResult Match<TResult>(
            Func<Completed, TResult> completed,
            Func<ConfigurationUnavailable, TResult> configurationUnavailable,
            Func<ProviderNotValidated, TResult> providerNotValidated
        )
        {
            return providerNotValidated(this);
        }
    }
}
