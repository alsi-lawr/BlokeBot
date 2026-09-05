using BlokeBot.Core.Features.ViewerPassports;

namespace BlokeBot.Core.Features.ViewerPortal;

/// <summary>The outcome of opening one viewer passport by login text.</summary>
public abstract record PortalPassportOutcome
{
    private PortalPassportOutcome() { }

    public abstract TResult Match<TResult>(
        Func<Visible, TResult> visible,
        Func<Hidden, TResult> hidden,
        Func<Unauthorized, TResult> unauthorized,
        Func<Ambiguous, TResult> ambiguous,
        Func<HistoricalLogin, TResult> historicalLogin,
        Func<NotFound, TResult> notFound,
        Func<FeatureDisabled, TResult> featureDisabled
    );

    public sealed record Visible(ViewerPassportView Passport) : PortalPassportOutcome
    {
        public override TResult Match<TResult>(
            Func<Visible, TResult> visible,
            Func<Hidden, TResult> hidden,
            Func<Unauthorized, TResult> unauthorized,
            Func<Ambiguous, TResult> ambiguous,
            Func<HistoricalLogin, TResult> historicalLogin,
            Func<NotFound, TResult> notFound,
            Func<FeatureDisabled, TResult> featureDisabled
        ) => visible(this);
    }

    public sealed record Hidden : PortalPassportOutcome
    {
        public override TResult Match<TResult>(
            Func<Visible, TResult> visible,
            Func<Hidden, TResult> hidden,
            Func<Unauthorized, TResult> unauthorized,
            Func<Ambiguous, TResult> ambiguous,
            Func<HistoricalLogin, TResult> historicalLogin,
            Func<NotFound, TResult> notFound,
            Func<FeatureDisabled, TResult> featureDisabled
        ) => hidden(this);
    }

    public sealed record Unauthorized : PortalPassportOutcome
    {
        public override TResult Match<TResult>(
            Func<Visible, TResult> visible,
            Func<Hidden, TResult> hidden,
            Func<Unauthorized, TResult> unauthorized,
            Func<Ambiguous, TResult> ambiguous,
            Func<HistoricalLogin, TResult> historicalLogin,
            Func<NotFound, TResult> notFound,
            Func<FeatureDisabled, TResult> featureDisabled
        ) => unauthorized(this);
    }

    public sealed record Ambiguous : PortalPassportOutcome
    {
        public override TResult Match<TResult>(
            Func<Visible, TResult> visible,
            Func<Hidden, TResult> hidden,
            Func<Unauthorized, TResult> unauthorized,
            Func<Ambiguous, TResult> ambiguous,
            Func<HistoricalLogin, TResult> historicalLogin,
            Func<NotFound, TResult> notFound,
            Func<FeatureDisabled, TResult> featureDisabled
        ) => ambiguous(this);
    }

    public sealed record HistoricalLogin : PortalPassportOutcome
    {
        public override TResult Match<TResult>(
            Func<Visible, TResult> visible,
            Func<Hidden, TResult> hidden,
            Func<Unauthorized, TResult> unauthorized,
            Func<Ambiguous, TResult> ambiguous,
            Func<HistoricalLogin, TResult> historicalLogin,
            Func<NotFound, TResult> notFound,
            Func<FeatureDisabled, TResult> featureDisabled
        ) => historicalLogin(this);
    }

    public sealed record NotFound : PortalPassportOutcome
    {
        public override TResult Match<TResult>(
            Func<Visible, TResult> visible,
            Func<Hidden, TResult> hidden,
            Func<Unauthorized, TResult> unauthorized,
            Func<Ambiguous, TResult> ambiguous,
            Func<HistoricalLogin, TResult> historicalLogin,
            Func<NotFound, TResult> notFound,
            Func<FeatureDisabled, TResult> featureDisabled
        ) => notFound(this);
    }

    public sealed record FeatureDisabled : PortalPassportOutcome
    {
        public override TResult Match<TResult>(
            Func<Visible, TResult> visible,
            Func<Hidden, TResult> hidden,
            Func<Unauthorized, TResult> unauthorized,
            Func<Ambiguous, TResult> ambiguous,
            Func<HistoricalLogin, TResult> historicalLogin,
            Func<NotFound, TResult> notFound,
            Func<FeatureDisabled, TResult> featureDisabled
        ) => featureDisabled(this);
    }
}
