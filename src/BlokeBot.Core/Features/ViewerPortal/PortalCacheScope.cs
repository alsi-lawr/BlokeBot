namespace BlokeBot.Core.Features.ViewerPortal;

/// <summary>The cache partition one portal response belongs to.</summary>
public abstract record PortalCacheScope
{
    private PortalCacheScope(PortalHostKey host) => Host = host;

    public abstract TResult Match<TResult>(
        Func<Public, TResult> @public,
        Func<Private, TResult> @private
    );

    public PortalHostKey Host { get; }

    public abstract string CacheControl { get; }

    // Only a session with no identity at all may share a cache entry. A stale or unavailable
    // session renders a notice that belongs to that request, so it is never shared either.
    public static PortalCacheScope For(PortalHostKey host, PortalIdentity identity) =>
        identity.Match<PortalCacheScope>(
            anonymous: _ => new Public(host),
            authenticated: _ => new Private(host),
            staleSession: _ => new Private(host),
            unavailableAuthentication: _ => new Private(host)
        );

    /// <summary>A shared, host-keyed partition.</summary>
    public sealed record Public : PortalCacheScope
    {
        public override TResult Match<TResult>(
            Func<Public, TResult> @public,
            Func<Private, TResult> @private
        ) => @public(this);

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
        public override TResult Match<TResult>(
            Func<Public, TResult> @public,
            Func<Private, TResult> @private
        ) => @private(this);

        public Private(PortalHostKey host)
            : base(host) { }

        public override string CacheControl => "no-store";
    }
}
