using System.Collections.Immutable;

namespace BlokeBot.Core.Features.ViewerPortal;

public sealed record PortalFeatureProjection(
    PortalFeatureDescriptor Descriptor,
    PortalSummaryOutcome Outcome
);

public sealed record PortalCatalogueSnapshot(
    PortalHostKey Host,
    PortalCacheScope CacheScope,
    ImmutableArray<PortalFeatureProjection> Features,
    ImmutableArray<PortalActivity> RecentActivity
);
