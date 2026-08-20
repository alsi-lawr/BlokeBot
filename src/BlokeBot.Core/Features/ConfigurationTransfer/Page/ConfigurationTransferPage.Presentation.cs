using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.ConfigurationTransfer.Page;

public partial class ConfigurationTransferPage
{
    private static string SectionTitle(ConfigurationSectionId section) =>
        section switch
        {
            ConfigurationSectionId.CustomCommands => "Custom commands",
            ConfigurationSectionId.Announcements => "Announcements",
            ConfigurationSectionId.Guessing => "Guessing game",
            ConfigurationSectionId.Points => "Points & giveaways",
            ConfigurationSectionId.ChannelToolEnablement => "Chat Tools enablement",
            _ => throw new ArgumentOutOfRangeException(nameof(section), section, null),
        };

    private static string SectionPath(ConfigurationSectionId section) =>
        section switch
        {
            ConfigurationSectionId.CustomCommands => "/custom-commands/settings",
            ConfigurationSectionId.Announcements => "/custom-commands#announcements",
            ConfigurationSectionId.Guessing => "/guessing/settings",
            ConfigurationSectionId.Points => "/points/settings",
            ConfigurationSectionId.ChannelToolEnablement => "/host#chat-tools",
            _ => "/configuration-transfer",
        };

    private static string CountSummary(ConfigurationPreviewCount counts) =>
        string.Join(
            " · ",
            new[]
            {
                counts.Add > 0 ? $"{counts.Add} add" : null,
                counts.Update > 0 ? $"{counts.Update} update" : null,
                counts.Skip > 0 ? $"{counts.Skip} skip" : null,
                counts.Remove > 0 ? $"{counts.Remove} remove" : null,
            }.Where(x => x is not null)
        );

    private static string StrategyTitle(ImportConflictStrategy strategy) =>
        strategy switch
        {
            ImportConflictStrategy.AddMissing => "Add missing",
            ImportConflictStrategy.Merge => "Merge by name",
            ImportConflictStrategy.ReplaceSection => "Replace section",
            _ => throw new ArgumentOutOfRangeException(nameof(strategy), strategy, null),
        };

    private static string ResolutionTitle(ImportConflictResolution resolution) =>
        resolution switch
        {
            ImportConflictResolution.Skip => "Skip whole item",
            ImportConflictResolution.Rename => "Rename imported alias",
            ImportConflictResolution.Replace => "Replace target item",
            ImportConflictResolution.Retain => "Retain target item",
            ImportConflictResolution.Abort => "Abort import",
            ImportConflictResolution.Unresolved => "Choose a decision",
            _ => throw new ArgumentOutOfRangeException(nameof(resolution), resolution, null),
        };

    private static string FeatureTitle(HostFeatureFlags feature) =>
        feature switch
        {
            HostFeatureFlags.Automations => "Automations",
            HostFeatureFlags.Polls => "Polls",
            HostFeatureFlags.ClipsAndMarkers => "Clips & markers",
            HostFeatureFlags.RewardsAndRedemptions => "Rewards & redemptions",
            HostFeatureFlags.Predictions => "Predictions",
            HostFeatureFlags.RequestBoards => "Request boards",
            HostFeatureFlags.PlayWithViewers => "Play with viewers",
            HostFeatureFlags.Moments => "Moments",
            HostFeatureFlags.Overlays => "Overlays",
            HostFeatureFlags.Guessing => "Guessing game",
            HostFeatureFlags.Points => "Points",
            HostFeatureFlags.Bounties => "Bounties",
            HostFeatureFlags.CommunityProgression => "Community progression",
            HostFeatureFlags.CooperativeGame => "BlokeRaid",
            HostFeatureFlags.ViewerPassports => "Viewer passports",
            HostFeatureFlags.Bingo => "Bingo",
            HostFeatureFlags.Competitions => "Tournaments & leagues",
            HostFeatureFlags.RaidCollaboration => "Raid & collaboration",
            HostFeatureFlags.Collectives => "Collectives",
            HostFeatureFlags.CustomCommands => "Custom commands",
            _ => throw new ArgumentOutOfRangeException(nameof(feature), feature, null),
        };

    private static string ActivationLabel(ConfigurationActivationStatus status) =>
        status switch
        {
            ConfigurationActivationStatus.Pending or ConfigurationActivationStatus.Processing =>
                "pending",
            ConfigurationActivationStatus.Complete => "complete",
            ConfigurationActivationStatus.Failed => "failed",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
        };

    private static string ActivationDescription(ConfigurationActivationStatus status) =>
        status switch
        {
            ConfigurationActivationStatus.Pending or ConfigurationActivationStatus.Processing =>
                "The import is complete. Selected lifecycle changes will run separately.",
            ConfigurationActivationStatus.Complete =>
                "Selected lifecycle changes are ready. Suppressed work was not replayed.",
            ConfigurationActivationStatus.Failed =>
                "The import remains saved. Retry only the separate activation step.",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
        };
}
