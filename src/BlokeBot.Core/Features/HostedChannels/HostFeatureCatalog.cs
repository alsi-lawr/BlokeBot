using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.HostedChannels;

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
                "Create chat commands, keep counters, and schedule messages.",
                enabledFeatures.Contains(HostFeatureFlags.CustomCommands)
            ),
            new(
                HostFeatureFlags.NativeTwitch,
                "Native Twitch",
                "Use shoutouts, polls, clips, and stream markers.",
                enabledFeatures.Contains(HostFeatureFlags.NativeTwitch)
            ),
            new(
                HostFeatureFlags.Overlays,
                "Overlays",
                "Manage Browser Sources for graphics shown on stream.",
                enabledFeatures.Contains(HostFeatureFlags.Overlays)
            ),
        ];
    }

    public static bool Contains(this HostFeatureFlags enabledFeatures, HostFeatureFlags feature)
    {
        return (enabledFeatures & feature) == feature;
    }
}
