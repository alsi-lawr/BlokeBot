using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.ConfigurationTransfer.Page;

internal static class ConfigurationTransferPresentation
{
    public static string SectionTitle(ConfigurationSectionId section) =>
        section switch
        {
            ConfigurationSectionId.CustomCommands => "Custom commands",
            ConfigurationSectionId.Announcements => "Announcements",
            ConfigurationSectionId.Guessing => "Guessing game",
            ConfigurationSectionId.Points => "Points & giveaways",
            ConfigurationSectionId.ChannelToolEnablement => "Chat Tools enablement",
            ConfigurationSectionId.Overlays => "Overlays",
            ConfigurationSectionId.Automations => "Automations",
            _ => throw new ArgumentOutOfRangeException(nameof(section), section, null),
        };

    public static string SectionPath(ConfigurationSectionId section) =>
        section switch
        {
            ConfigurationSectionId.CustomCommands => "/custom-commands/settings",
            ConfigurationSectionId.Announcements => "/custom-commands#announcements",
            ConfigurationSectionId.Guessing => "/guessing/settings",
            ConfigurationSectionId.Points => "/points/settings",
            ConfigurationSectionId.ChannelToolEnablement => "/host#chat-tools",
            ConfigurationSectionId.Overlays => "/overlays",
            ConfigurationSectionId.Automations => "/automations",
            _ => "/configuration-transfer",
        };

    public static string StrategyTitle(ImportConflictStrategy strategy) =>
        strategy switch
        {
            ImportConflictStrategy.AddMissing => "Add missing",
            ImportConflictStrategy.Merge => "Merge by name",
            ImportConflictStrategy.ReplaceSection => "Replace section",
            _ => throw new ArgumentOutOfRangeException(nameof(strategy), strategy, null),
        };

    public static string ResolutionTitle(ImportConflictResolution resolution) =>
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

    public static string FeatureTitle(HostFeatureFlags feature) =>
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

    public static string ActivationLabel(ConfigurationActivationStatus status) =>
        status switch
        {
            ConfigurationActivationStatus.Pending or ConfigurationActivationStatus.Processing =>
                "pending",
            ConfigurationActivationStatus.Complete => "complete",
            ConfigurationActivationStatus.Failed => "failed",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
        };

    public static string ActivationDescription(ConfigurationActivationStatus status) =>
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

    public static string ActivationPillClass(ConfigurationActivationStatus status) =>
        status switch
        {
            ConfigurationActivationStatus.Pending or ConfigurationActivationStatus.Processing =>
                "status-pill status-pill--amber",
            ConfigurationActivationStatus.Complete => "status-pill status-pill--green",
            ConfigurationActivationStatus.Failed => "status-pill status-pill--red",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
        };

    public static string ConflictKey(ConfigurationImportConflict conflict) =>
        $"{conflict.Section}:{conflict.ImportedId}";
}
