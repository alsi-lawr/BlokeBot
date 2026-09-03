using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.ViewerPassports;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.ViewerPortal;

/// <summary>The exact host one portal query is scoped to.</summary>
public readonly record struct PortalHostKey(int Id, string Login);

/// <summary>A channel resolved from the canonical portal route.</summary>
public sealed record PortalChannel(
    PortalHostKey Host,
    string DisplayName,
    HostFeatureFlags EnabledFeatures
)
{
    /// <summary>The enabled features with a public surface, in catalogue order.</summary>
    public IReadOnlyList<HostFeatureFlags> PublicFeatures =>
        ViewerPortalFeatures.PublicFeatures(EnabledFeatures);
}

/// <summary>The outcome of resolving a portal route login.</summary>
public abstract record PortalChannelOutcome
{
    private PortalChannelOutcome() { }

    public sealed record Resolved(PortalChannel Channel) : PortalChannelOutcome;

    public sealed record NotFound : PortalChannelOutcome;
}

/// <summary>The portal-facing shape of the site session.</summary>
public enum PortalIdentityPresentation
{
    Anonymous,
    Authenticated,
    Unavailable,
}

/// <summary>The site session as the portal classifies it.</summary>
public abstract record PortalIdentity
{
    private PortalIdentity() { }

    public abstract PortalIdentityPresentation Presentation { get; }

    public sealed record Anonymous : PortalIdentity
    {
        public override PortalIdentityPresentation Presentation =>
            PortalIdentityPresentation.Anonymous;
    }

    public sealed record Authenticated(string TwitchUserId, string Login, string DisplayName)
        : PortalIdentity
    {
        public override PortalIdentityPresentation Presentation =>
            PortalIdentityPresentation.Authenticated;
    }

    public sealed record StaleSession : PortalIdentity
    {
        public override PortalIdentityPresentation Presentation =>
            PortalIdentityPresentation.Unavailable;
    }

    public sealed record UnavailableAuthentication : PortalIdentity
    {
        public override PortalIdentityPresentation Presentation =>
            PortalIdentityPresentation.Unavailable;
    }
}

/// <summary>An authenticated viewer bound to exactly one host.</summary>
public sealed record PortalViewer(
    PortalHostKey Host,
    string TwitchUserId,
    string Login,
    string DisplayName
);

/// <summary>The outcome of binding the session to its own host-scoped views.</summary>
public abstract record PortalSelfOutcome
{
    private PortalSelfOutcome() { }

    public sealed record Anonymous : PortalSelfOutcome;

    public sealed record AuthenticatedSelf(PortalViewer Viewer) : PortalSelfOutcome;

    public sealed record Renamed(PortalViewer Viewer) : PortalSelfOutcome;

    public sealed record Erased : PortalSelfOutcome;

    public sealed record StaleSession : PortalSelfOutcome;

    public sealed record UnavailableAuthentication : PortalSelfOutcome;
}

/// <summary>The outcome of opening one viewer passport by login text.</summary>
public abstract record PortalPassportOutcome
{
    private PortalPassportOutcome() { }

    public sealed record Visible(ViewerPassportView Passport) : PortalPassportOutcome;

    public sealed record Hidden : PortalPassportOutcome;

    public sealed record Unauthorized : PortalPassportOutcome;

    public sealed record Ambiguous : PortalPassportOutcome;

    public sealed record HistoricalLogin : PortalPassportOutcome;

    public sealed record NotFound : PortalPassportOutcome;

    public sealed record FeatureDisabled : PortalPassportOutcome;
}

/// <summary>The cache partition one portal response belongs to.</summary>
public abstract record PortalCacheScope
{
    private PortalCacheScope(PortalHostKey host) => Host = host;

    public PortalHostKey Host { get; }

    public abstract string CacheControl { get; }

    // Only a session with no identity at all may share a cache entry. A stale or unavailable
    // session renders a notice that belongs to that request, so it is never shared either.
    public static PortalCacheScope For(PortalHostKey host, PortalIdentity identity) =>
        identity is PortalIdentity.Anonymous ? new Public(host) : new Private(host);

    /// <summary>A shared, host-keyed partition.</summary>
    public sealed record Public : PortalCacheScope
    {
        public Public(PortalHostKey host)
            : base(host) { }

        // The key is the host id alone, so an entry can never hold a viewer value or serve
        // another host.
        public string Key => $"viewer-portal:{Host.Id}:public";

        public override string CacheControl => "public";
    }

    /// <summary>A partition that is never stored.</summary>
    public sealed record Private : PortalCacheScope
    {
        public Private(PortalHostKey host)
            : base(host) { }

        public override string CacheControl => "no-store";
    }
}

/// <summary>The host feature flags with a public page.</summary>
public static class ViewerPortalFeatures
{
    private const HostFeatureFlags _publicSurface =
        HostFeatureFlags.RequestBoards
        | HostFeatureFlags.PlayWithViewers
        | HostFeatureFlags.Moments
        | HostFeatureFlags.Guessing
        | HostFeatureFlags.Points
        | HostFeatureFlags.Bounties
        | HostFeatureFlags.CommunityProgression
        | HostFeatureFlags.CooperativeGame
        | HostFeatureFlags.ViewerPassports
        | HostFeatureFlags.Bingo
        | HostFeatureFlags.Competitions
        | HostFeatureFlags.Collectives;

    // Feature dependencies such as Bounties requiring Points stay with the owning feature, which
    // reports itself disabled when the portal reads its summary.
    public static IReadOnlyList<HostFeatureFlags> PublicFeatures(HostFeatureFlags enabled) =>
        HostFeatureCatalog
            .Features.Where(feature =>
                _publicSurface.Contains(feature) && enabled.Contains(feature)
            )
            .ToArray();
}
