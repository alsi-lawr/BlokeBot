using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.ViewerPortal;

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
