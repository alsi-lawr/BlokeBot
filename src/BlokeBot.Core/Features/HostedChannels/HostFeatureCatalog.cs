using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.HostedChannels;

public static class HostFeatureCatalog
{
    public static IReadOnlyList<HostFeatureFlags> Features { get; } =
    [
        HostFeatureFlags.Automations,
        HostFeatureFlags.Shoutouts,
        HostFeatureFlags.Polls,
        HostFeatureFlags.ClipsAndMarkers,
        HostFeatureFlags.RewardsAndRedemptions,
        HostFeatureFlags.Predictions,
        HostFeatureFlags.RequestBoards,
        HostFeatureFlags.PlayWithViewers,
        HostFeatureFlags.Moments,
        HostFeatureFlags.Overlays,
        HostFeatureFlags.Guessing,
        HostFeatureFlags.Points,
        HostFeatureFlags.Bounties,
        HostFeatureFlags.CommunityProgression,
        HostFeatureFlags.Bingo,
        HostFeatureFlags.Competitions,
        HostFeatureFlags.CustomCommands,
    ];

    public static IReadOnlyList<HostFeatureCardState> Cards(HostFeatureFlags enabledFeatures) =>
        [
            new(
                HostFeatureFlags.Shoutouts,
                "Shoutouts",
                "Send manual and automatic raid shoutouts.",
                enabledFeatures.Contains(HostFeatureFlags.Shoutouts)
            ),
            new(
                HostFeatureFlags.Polls,
                "Polls",
                "Create and manage native Twitch polls.",
                enabledFeatures.Contains(HostFeatureFlags.Polls)
            ),
            new(
                HostFeatureFlags.ClipsAndMarkers,
                "Clips & markers",
                "Create native Twitch clips and stream markers.",
                enabledFeatures.Contains(HostFeatureFlags.ClipsAndMarkers)
            ),
            new(
                HostFeatureFlags.RewardsAndRedemptions,
                "Rewards & redemptions",
                "Manage channel point rewards and redemptions.",
                enabledFeatures.Contains(HostFeatureFlags.RewardsAndRedemptions)
            ),
            new(
                HostFeatureFlags.Predictions,
                "Predictions",
                "Create and manage native Twitch predictions.",
                enabledFeatures.Contains(HostFeatureFlags.Predictions)
            ),
            new(
                HostFeatureFlags.RequestBoards,
                "Request boards",
                "Collect, vote on, and moderate viewer requests.",
                enabledFeatures.Contains(HostFeatureFlags.RequestBoards)
            ),
            new(
                HostFeatureFlags.PlayWithViewers,
                "Play with viewers",
                "Run viewer queues and private lobby delivery.",
                enabledFeatures.Contains(HostFeatureFlags.PlayWithViewers)
            ),
            new(
                HostFeatureFlags.Moments,
                "Moments",
                "Capture, review, and publish stream moments.",
                enabledFeatures.Contains(HostFeatureFlags.Moments)
            ),
            new(
                HostFeatureFlags.Overlays,
                "Overlays",
                "Manage Browser Sources for graphics shown on stream.",
                enabledFeatures.Contains(HostFeatureFlags.Overlays)
            ),
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
                HostFeatureFlags.Bounties,
                "Bounties",
                "Let viewers fund channel challenges with points. Requires Points to be on.",
                enabledFeatures.Contains(HostFeatureFlags.Bounties)
            ),
            new(
                HostFeatureFlags.CommunityProgression,
                "Community progression",
                "Run seasons with quests, achievements, standings, and persistent rewards.",
                enabledFeatures.Contains(HostFeatureFlags.CommunityProgression)
            ),
            new(
                HostFeatureFlags.Bingo,
                "Bingo",
                "Run shared, viewer, or team cards from typed stream events.",
                enabledFeatures.Contains(HostFeatureFlags.Bingo)
            ),
            new(
                HostFeatureFlags.Competitions,
                "Tournaments & leagues",
                "Run viewer brackets, round-robin leagues, and prediction leagues.",
                enabledFeatures.Contains(HostFeatureFlags.Competitions)
            ),
            new(
                HostFeatureFlags.CustomCommands,
                "Custom commands",
                "Create chat commands, keep counters, and schedule messages.",
                enabledFeatures.Contains(HostFeatureFlags.CustomCommands)
            ),
        ];

    public static bool Contains(this HostFeatureFlags enabledFeatures, HostFeatureFlags feature) =>
        (enabledFeatures & feature) == feature;

    public static bool IsSelectable(HostFeatureFlags feature) => Features.Contains(feature);
}
