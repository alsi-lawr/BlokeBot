using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.ViewerPortal;

public enum PortalCategory
{
    Activity,
    Community,
    Leaderboard,
    Personal,
}

public enum PortalIcon
{
    Bingo,
    Queue,
    Bounty,
    Raid,
    Competition,
    Community,
    Request,
    Moment,
    Points,
    Guessing,
    Collective,
    Passport,
}

public enum PortalAudience
{
    Public,
    Self,
}

public sealed class PortalFeatureDescriptor
{
    private readonly Func<PortalHostKey, string>? _fallbackRoute;

    internal PortalFeatureDescriptor(
        HostFeatureFlags feature,
        PortalIcon icon,
        PortalCategory category,
        PortalAudience audience,
        Func<
            PortalProjectors,
            PortalChannel,
            PortalIdentity,
            CancellationToken,
            Task<PortalSummaryOutcome>
        > project,
        Func<PortalHostKey, string>? fallbackRoute = null
    )
    {
        var metadata = HostFeatureCatalog
            .Cards(HostFeatureFlags.None)
            .Single(card => card.Feature == feature);
        Feature = metadata.Feature;
        Label = metadata.Name;
        Icon = icon;
        Category = category;
        Audience = audience;
        ProjectAsync = project;
        _fallbackRoute = fallbackRoute;
    }

    public HostFeatureFlags Feature { get; }
    public string Label { get; }
    public PortalIcon Icon { get; }
    public PortalCategory Category { get; }
    public PortalAudience Audience { get; }

    /// <summary>Returns a channel or self destination without a loaded summary. Resource-specific
    /// destinations and features requiring owner admission have no fallback.</summary>
    public PortalLink? GetFallbackLink(PortalHostKey host, PortalIdentity identity)
    {
        if (_fallbackRoute is null)
        {
            return null;
        }
        var admitted = Audience switch
        {
            PortalAudience.Public => true,
            PortalAudience.Self => identity.Match(
                anonymous: static _ => false,
                authenticated: static _ => true,
                staleSession: static _ => false,
                unavailableAuthentication: static _ => false
            ),
        };
        return admitted ? PortalSummaryBounds.Link($"Open {Label}", _fallbackRoute(host)) : null;
    }

    internal Func<
        PortalProjectors,
        PortalChannel,
        PortalIdentity,
        CancellationToken,
        Task<PortalSummaryOutcome>
    > ProjectAsync { get; }
}
