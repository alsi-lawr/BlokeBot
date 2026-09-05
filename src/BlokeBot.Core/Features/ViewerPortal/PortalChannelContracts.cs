using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.ViewerPortal;

/// <summary>The exact host one portal query is scoped to.</summary>
public readonly record struct PortalHostKey(int Id, string Login);

/// <summary>A channel resolved from the canonical portal route.</summary>
public sealed record PortalChannel(
    PortalHostKey Host,
    string DisplayName,
    IReadOnlyList<HostFeatureFlags> PublicFeatures
);

/// <summary>The outcome of resolving a portal route login.</summary>
public abstract record PortalChannelOutcome
{
    private PortalChannelOutcome() { }

    public abstract TResult Match<TResult>(
        Func<Resolved, TResult> resolved,
        Func<NotFound, TResult> notFound
    );

    public sealed record Resolved(PortalChannel Channel) : PortalChannelOutcome
    {
        public override TResult Match<TResult>(
            Func<Resolved, TResult> resolved,
            Func<NotFound, TResult> notFound
        ) => resolved(this);
    }

    public sealed record NotFound : PortalChannelOutcome
    {
        public override TResult Match<TResult>(
            Func<Resolved, TResult> resolved,
            Func<NotFound, TResult> notFound
        ) => notFound(this);
    }
}
