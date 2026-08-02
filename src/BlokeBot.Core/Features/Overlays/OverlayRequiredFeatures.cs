using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.Overlays;

internal static class OverlayRequiredFeatures
{
    internal static HostFeatureFlags For(OverlayType type) =>
        type switch
        {
            OverlayType.Guessing => HostFeatureFlags.Overlays | HostFeatureFlags.Guessing,
            OverlayType.Giveaway => HostFeatureFlags.Overlays | HostFeatureFlags.Points,
            _ => HostFeatureFlags.Overlays,
        };

    internal static bool AreEnabled(OverlayType type, HostFeatureFlags enabledFeatures)
    {
        var required = For(type);
        return (enabledFeatures & required) == required;
    }
}
