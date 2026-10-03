using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.Automations;

internal static class AutomationRequiredFeatures
{
    internal static HostFeatureFlags BackingFeatures(HostFeatureFlags required) =>
        required & ~HostFeatureFlags.Automations;

    internal static HostFeatureFlags ForDefinitions(IEnumerable<string> definitionIds) =>
        definitionIds.Aggregate(
            HostFeatureFlags.Automations,
            static (required, definitionId) =>
                required
                | (
                    definitionId switch
                    {
                        "custom-command" => HostFeatureFlags.CustomCommands,
                        "play-overlay-cue" or "cue-lifecycle" => HostFeatureFlags.Overlays,
                        "reward-redemption"
                        or "redemption-updated"
                        or "fulfil-redemption"
                        or "cancel-redemption" => HostFeatureFlags.RewardsAndRedemptions,
                        "giveaway-lifecycle" => HostFeatureFlags.Points,
                        "guessing-lifecycle" => HostFeatureFlags.Guessing,
                        "queue-lifecycle" => HostFeatureFlags.PlayWithViewers,
                        "outgoing-raid" => HostFeatureFlags.RaidCollaboration,
                        _ => NativeOperationAutomations.BackingFeature(definitionId),
                    }
                )
        );
}
