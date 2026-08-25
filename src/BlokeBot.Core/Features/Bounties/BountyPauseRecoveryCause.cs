using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.Bounties;

internal sealed record BountyPauseRecoveryCause
{
    private readonly string _description;

    private BountyPauseRecoveryCause(string description) => _description = description;

    internal static BountyPauseRecoveryCause FeatureChanged(
        HostFeatureFlags feature,
        HostFeatureActivationState state
    ) =>
        new(
            $"the {FeatureName(feature)} feature was {StateText(state)} and bounty dependencies became active"
        );

    internal static BountyPauseRecoveryCause Restart() =>
        new("restart recovery found the bounty dependencies active");

    internal string Describe() => _description;

    private static string FeatureName(HostFeatureFlags feature) =>
        HostFeatureCatalog.Cards(feature).Single(card => card.Feature == feature).Name;

    private static string StateText(HostFeatureActivationState state) =>
        state switch
        {
            HostFeatureActivationState.Disabled => "disabled",
            HostFeatureActivationState.Enabled => "enabled",
        };
}
