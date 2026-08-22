using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.ConfigurationTransfer;

internal static class ChannelToolEnablementMapper
{
    private const HostFeatureFlags _retiredShoutouts = (HostFeatureFlags)(1UL << 3);

    public static bool CanRepresent(HostFeatureFlags flags) =>
        (flags & ~(HostFeatureFlags.All | _retiredShoutouts)) == 0;

    public static ChannelToolEnablementV1 FromFlags(HostFeatureFlags flags) =>
        new(
            Has(flags, HostFeatureFlags.Automations),
            Has(flags, HostFeatureFlags.Polls),
            Has(flags, HostFeatureFlags.ClipsAndMarkers),
            Has(flags, HostFeatureFlags.RewardsAndRedemptions),
            Has(flags, HostFeatureFlags.Predictions),
            Has(flags, HostFeatureFlags.RequestBoards),
            Has(flags, HostFeatureFlags.PlayWithViewers),
            Has(flags, HostFeatureFlags.Moments),
            Has(flags, HostFeatureFlags.Overlays),
            Has(flags, HostFeatureFlags.Guessing),
            Has(flags, HostFeatureFlags.Points),
            Has(flags, HostFeatureFlags.Bounties),
            Has(flags, HostFeatureFlags.CommunityProgression),
            Has(flags, HostFeatureFlags.CooperativeGame),
            Has(flags, HostFeatureFlags.ViewerPassports),
            Has(flags, HostFeatureFlags.Bingo),
            Has(flags, HostFeatureFlags.Competitions),
            Has(flags, HostFeatureFlags.RaidCollaboration),
            Has(flags, HostFeatureFlags.Collectives),
            Has(flags, HostFeatureFlags.CustomCommands)
        );

    public static HostFeatureFlags ToFlags(ChannelToolEnablementV1 value) =>
        Flag(value.Automations, HostFeatureFlags.Automations)
        | Flag(value.Polls, HostFeatureFlags.Polls)
        | Flag(value.ClipsAndMarkers, HostFeatureFlags.ClipsAndMarkers)
        | Flag(value.RewardsAndRedemptions, HostFeatureFlags.RewardsAndRedemptions)
        | Flag(value.Predictions, HostFeatureFlags.Predictions)
        | Flag(value.RequestBoards, HostFeatureFlags.RequestBoards)
        | Flag(value.PlayWithViewers, HostFeatureFlags.PlayWithViewers)
        | Flag(value.Moments, HostFeatureFlags.Moments)
        | Flag(value.Overlays, HostFeatureFlags.Overlays)
        | Flag(value.Guessing, HostFeatureFlags.Guessing)
        | Flag(value.Points, HostFeatureFlags.Points)
        | Flag(value.Bounties, HostFeatureFlags.Bounties)
        | Flag(value.CommunityProgression, HostFeatureFlags.CommunityProgression)
        | Flag(value.CooperativeGame, HostFeatureFlags.CooperativeGame)
        | Flag(value.ViewerPassports, HostFeatureFlags.ViewerPassports)
        | Flag(value.Bingo, HostFeatureFlags.Bingo)
        | Flag(value.Competitions, HostFeatureFlags.Competitions)
        | Flag(value.RaidCollaboration, HostFeatureFlags.RaidCollaboration)
        | Flag(value.Collectives, HostFeatureFlags.Collectives)
        | Flag(value.CustomCommands, HostFeatureFlags.CustomCommands);

    public static IReadOnlyList<(HostFeatureFlags Feature, bool Enabled)> Changes(
        HostFeatureFlags current,
        ChannelToolEnablementV1 imported
    ) =>
        HostFeatureCatalog
            .Features.Where(feature => Has(current, feature) != Has(ToFlags(imported), feature))
            .Select(feature => (feature, Has(ToFlags(imported), feature)))
            .ToArray();

    private static bool Has(HostFeatureFlags flags, HostFeatureFlags value) =>
        (flags & value) == value;

    private static HostFeatureFlags Flag(bool enabled, HostFeatureFlags value) =>
        enabled ? value : HostFeatureFlags.None;
}
