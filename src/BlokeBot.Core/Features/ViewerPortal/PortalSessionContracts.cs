namespace BlokeBot.Core.Features.ViewerPortal;

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

    public abstract TResult Match<TResult>(
        Func<Anonymous, TResult> anonymous,
        Func<Authenticated, TResult> authenticated,
        Func<StaleSession, TResult> staleSession,
        Func<UnavailableAuthentication, TResult> unavailableAuthentication
    );

    public abstract PortalIdentityPresentation Presentation { get; }

    public sealed record Anonymous : PortalIdentity
    {
        public override TResult Match<TResult>(
            Func<Anonymous, TResult> anonymous,
            Func<Authenticated, TResult> authenticated,
            Func<StaleSession, TResult> staleSession,
            Func<UnavailableAuthentication, TResult> unavailableAuthentication
        ) => anonymous(this);

        public override PortalIdentityPresentation Presentation =>
            PortalIdentityPresentation.Anonymous;
    }

    public sealed record Authenticated(string TwitchUserId, string Login, string DisplayName)
        : PortalIdentity
    {
        public override TResult Match<TResult>(
            Func<Anonymous, TResult> anonymous,
            Func<Authenticated, TResult> authenticated,
            Func<StaleSession, TResult> staleSession,
            Func<UnavailableAuthentication, TResult> unavailableAuthentication
        ) => authenticated(this);

        public override PortalIdentityPresentation Presentation =>
            PortalIdentityPresentation.Authenticated;
    }

    public sealed record StaleSession : PortalIdentity
    {
        public override TResult Match<TResult>(
            Func<Anonymous, TResult> anonymous,
            Func<Authenticated, TResult> authenticated,
            Func<StaleSession, TResult> staleSession,
            Func<UnavailableAuthentication, TResult> unavailableAuthentication
        ) => staleSession(this);

        public override PortalIdentityPresentation Presentation =>
            PortalIdentityPresentation.Unavailable;
    }

    public sealed record UnavailableAuthentication : PortalIdentity
    {
        public override TResult Match<TResult>(
            Func<Anonymous, TResult> anonymous,
            Func<Authenticated, TResult> authenticated,
            Func<StaleSession, TResult> staleSession,
            Func<UnavailableAuthentication, TResult> unavailableAuthentication
        ) => unavailableAuthentication(this);

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

    public abstract TResult Match<TResult>(
        Func<Anonymous, TResult> anonymous,
        Func<AuthenticatedSelf, TResult> authenticatedSelf,
        Func<Renamed, TResult> renamed,
        Func<Erased, TResult> erased,
        Func<StaleSession, TResult> staleSession,
        Func<UnavailableAuthentication, TResult> unavailableAuthentication
    );

    public sealed record Anonymous : PortalSelfOutcome
    {
        public override TResult Match<TResult>(
            Func<Anonymous, TResult> anonymous,
            Func<AuthenticatedSelf, TResult> authenticatedSelf,
            Func<Renamed, TResult> renamed,
            Func<Erased, TResult> erased,
            Func<StaleSession, TResult> staleSession,
            Func<UnavailableAuthentication, TResult> unavailableAuthentication
        ) => anonymous(this);
    }

    public sealed record AuthenticatedSelf(PortalViewer Viewer) : PortalSelfOutcome
    {
        public override TResult Match<TResult>(
            Func<Anonymous, TResult> anonymous,
            Func<AuthenticatedSelf, TResult> authenticatedSelf,
            Func<Renamed, TResult> renamed,
            Func<Erased, TResult> erased,
            Func<StaleSession, TResult> staleSession,
            Func<UnavailableAuthentication, TResult> unavailableAuthentication
        ) => authenticatedSelf(this);
    }

    public sealed record Renamed(PortalViewer Viewer) : PortalSelfOutcome
    {
        public override TResult Match<TResult>(
            Func<Anonymous, TResult> anonymous,
            Func<AuthenticatedSelf, TResult> authenticatedSelf,
            Func<Renamed, TResult> renamed,
            Func<Erased, TResult> erased,
            Func<StaleSession, TResult> staleSession,
            Func<UnavailableAuthentication, TResult> unavailableAuthentication
        ) => renamed(this);
    }

    public sealed record Erased : PortalSelfOutcome
    {
        public override TResult Match<TResult>(
            Func<Anonymous, TResult> anonymous,
            Func<AuthenticatedSelf, TResult> authenticatedSelf,
            Func<Renamed, TResult> renamed,
            Func<Erased, TResult> erased,
            Func<StaleSession, TResult> staleSession,
            Func<UnavailableAuthentication, TResult> unavailableAuthentication
        ) => erased(this);
    }

    public sealed record StaleSession : PortalSelfOutcome
    {
        public override TResult Match<TResult>(
            Func<Anonymous, TResult> anonymous,
            Func<AuthenticatedSelf, TResult> authenticatedSelf,
            Func<Renamed, TResult> renamed,
            Func<Erased, TResult> erased,
            Func<StaleSession, TResult> staleSession,
            Func<UnavailableAuthentication, TResult> unavailableAuthentication
        ) => staleSession(this);
    }

    public sealed record UnavailableAuthentication : PortalSelfOutcome
    {
        public override TResult Match<TResult>(
            Func<Anonymous, TResult> anonymous,
            Func<AuthenticatedSelf, TResult> authenticatedSelf,
            Func<Renamed, TResult> renamed,
            Func<Erased, TResult> erased,
            Func<StaleSession, TResult> staleSession,
            Func<UnavailableAuthentication, TResult> unavailableAuthentication
        ) => unavailableAuthentication(this);
    }
}
