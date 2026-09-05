namespace BlokeBot.Core.Features.ViewerPortal;

public abstract record PortalSummaryOutcome
{
    private PortalSummaryOutcome() { }

    public abstract TResult Match<TResult>(
        Func<Available, TResult> available,
        Func<Empty, TResult> empty,
        Func<Disabled, TResult> disabled,
        Func<Degraded, TResult> degraded,
        Func<Unavailable, TResult> unavailable,
        Func<Unauthorized, TResult> unauthorized
    );

    public sealed record Available(PortalSummary Summary) : PortalSummaryOutcome
    {
        public override TResult Match<TResult>(
            Func<Available, TResult> available,
            Func<Empty, TResult> empty,
            Func<Disabled, TResult> disabled,
            Func<Degraded, TResult> degraded,
            Func<Unavailable, TResult> unavailable,
            Func<Unauthorized, TResult> unauthorized
        ) => available(this);
    }

    public sealed record Empty(PortalSummary Summary) : PortalSummaryOutcome
    {
        public override TResult Match<TResult>(
            Func<Available, TResult> available,
            Func<Empty, TResult> empty,
            Func<Disabled, TResult> disabled,
            Func<Degraded, TResult> degraded,
            Func<Unavailable, TResult> unavailable,
            Func<Unauthorized, TResult> unauthorized
        ) => empty(this);
    }

    public sealed record Disabled : PortalSummaryOutcome
    {
        public override TResult Match<TResult>(
            Func<Available, TResult> available,
            Func<Empty, TResult> empty,
            Func<Disabled, TResult> disabled,
            Func<Degraded, TResult> degraded,
            Func<Unavailable, TResult> unavailable,
            Func<Unauthorized, TResult> unauthorized
        ) => disabled(this);
    }

    public sealed record Degraded(PortalSummary Summary) : PortalSummaryOutcome
    {
        public override TResult Match<TResult>(
            Func<Available, TResult> available,
            Func<Empty, TResult> empty,
            Func<Disabled, TResult> disabled,
            Func<Degraded, TResult> degraded,
            Func<Unavailable, TResult> unavailable,
            Func<Unauthorized, TResult> unauthorized
        ) => degraded(this);
    }

    public sealed record Unavailable : PortalSummaryOutcome
    {
        public override TResult Match<TResult>(
            Func<Available, TResult> available,
            Func<Empty, TResult> empty,
            Func<Disabled, TResult> disabled,
            Func<Degraded, TResult> degraded,
            Func<Unavailable, TResult> unavailable,
            Func<Unauthorized, TResult> unauthorized
        ) => unavailable(this);
    }

    public sealed record Unauthorized : PortalSummaryOutcome
    {
        public override TResult Match<TResult>(
            Func<Available, TResult> available,
            Func<Empty, TResult> empty,
            Func<Disabled, TResult> disabled,
            Func<Degraded, TResult> degraded,
            Func<Unavailable, TResult> unavailable,
            Func<Unauthorized, TResult> unauthorized
        ) => unauthorized(this);
    }
}
