using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.Automations;

internal static class AutomationRequiredFeatures
{
    internal static HostFeatureFlags ForDefinitions(IEnumerable<string> definitionIds) =>
        definitionIds.Aggregate(
            HostFeatureFlags.Automations,
            static (required, definitionId) =>
                required
                | (
                    definitionId switch
                    {
                        "custom-command" => HostFeatureFlags.CustomCommands,
                        "play-overlay-cue" => HostFeatureFlags.Overlays,
                        _ => HostFeatureFlags.None,
                    }
                )
        );
}
