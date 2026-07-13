using BlokeBot.Persistence.Models;

namespace BlokeBot.Features.HostedChannels;

public static class HostFeatureCatalog
{
    public static IReadOnlyList<HostFeatureCardState> Cards(HostFeatureFlags enabledFeatures)
    {
        return
        [
            new(
                HostFeatureFlags.Guessing,
                "Guessing game",
                "Let chat guess live results and see past rounds.",
                enabledFeatures.Contains(HostFeatureFlags.Guessing)
            ),
            new(
                HostFeatureFlags.Points,
                "Points",
                "Track viewer points, run giveaways, and let moderators change balances.",
                enabledFeatures.Contains(HostFeatureFlags.Points)
            ),
            new(
                HostFeatureFlags.CustomCommands,
                "Custom commands",
                "Create chat commands, keep counters, and send automatic announcements.",
                enabledFeatures.Contains(HostFeatureFlags.CustomCommands)
            ),
        ];
    }

    public static bool Contains(this HostFeatureFlags enabledFeatures, HostFeatureFlags feature)
    {
        return (enabledFeatures & feature) == feature;
    }
}
