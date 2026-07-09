using BlokeBot.Persistence.Models;

namespace BlokeBot.Features.HostedChannels;

public static class HostFeatureCatalog
{
    public static IReadOnlyList<HostFeatureCardState> Cards(HostFeatureFlags enabledFeatures) =>
        [
            new(
                HostFeatureFlags.Guessing,
                "Guessing game",
                "Let chat guess live results and keep a round history.",
                enabledFeatures.Contains(HostFeatureFlags.Guessing)
            ),
            new(
                HostFeatureFlags.Points,
                "Points",
                "Track viewer points, giveaways, gambling, and moderator adjustments.",
                enabledFeatures.Contains(HostFeatureFlags.Points)
            ),
        ];

    public static bool Contains(this HostFeatureFlags enabledFeatures, HostFeatureFlags feature) =>
        (enabledFeatures & feature) == feature;
}
