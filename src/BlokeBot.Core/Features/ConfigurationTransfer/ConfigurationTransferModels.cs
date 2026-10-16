using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.ConfigurationTransfer;

public enum ConfigurationSectionId
{
    CustomCommands,
    Announcements,
    Guessing,
    Points,
    ChannelToolEnablement,
    Overlays,
    Automations,
    Dashboards,
}

public sealed record OverlayExportSelection(
    bool UrlLayers,
    bool MediaDocumentLinks,
    bool UrlWarningAcknowledged
);

public sealed record ConfigurationExportSelection(
    IReadOnlySet<ConfigurationSectionId> Sections,
    OverlayExportSelection Overlay
)
{
    public IReadOnlySet<Guid> AutomationFlowIds { get; init; } = new HashSet<Guid>();
    public IReadOnlySet<Guid> AutomationScenarioIds { get; init; } = new HashSet<Guid>();
}

public enum ImportConflictStrategy
{
    AddMissing,
    Merge,
    ReplaceSection,
}

public enum ImportConflictResolution
{
    Unresolved,
    Skip,
    Rename,
    Replace,
    Retain,
    Abort,
}

public sealed record ImportItemResolution(
    string ImportedId,
    ImportConflictResolution Resolution,
    string? ReplacementName = null,
    int? TargetId = null
);

public sealed record SectionImportSelection(
    ConfigurationSectionId Section,
    ImportConflictStrategy Strategy,
    IReadOnlyList<ImportItemResolution> ItemResolutions
)
{
    public AutomationImportReview? AutomationReview { get; init; }
    public bool AutomationEffectsApproved { get; init; }
}

public sealed record ConfigurationImportSelection(
    int DestinationHostId,
    IReadOnlyList<SectionImportSelection> Sections,
    IReadOnlySet<HostFeatureFlags> EnablementChanges
);

public sealed record ConfigurationPreviewCount(int Add, int Update, int Skip, int Remove);

public sealed record ConfigurationValidationIssue(
    string Location,
    string Message,
    bool BlocksApply = true
);

public sealed record ConfigurationImportConflict(
    ConfigurationSectionId Section,
    string ImportedId,
    string ItemName,
    string Message,
    IReadOnlyList<ImportConflictResolution> AllowedResolutions
);

public sealed record ConfigurationSectionPreview(
    ConfigurationSectionId Section,
    ConfigurationPreviewCount Counts,
    IReadOnlyList<ConfigurationValidationIssue> Issues,
    IReadOnlyList<ConfigurationImportConflict> Conflicts
)
{
    public IReadOnlyList<GuessingProfileMappingPreview> GuessingProfileMappings { get; init; } = [];
    public AutomationImportReview? AutomationReview { get; init; }
}

public sealed record GuessingProfileMappingPreview(
    string ImportedProfileId,
    string ImportedProfileName,
    int? AutomaticTargetId,
    IReadOnlyList<GuessingProfileTargetChoice> ExistingTargets
);

public sealed record GuessingProfileTargetChoice(int TargetId, string Name, string Slug);

public sealed record ConfigurationImportPreview(
    Guid PreviewId,
    int DestinationHostId,
    string DestinationLogin,
    ConfigurationDocumentV2 Document,
    IReadOnlyList<ConfigurationSectionPreview> Sections,
    IReadOnlyList<ConfigurationEnablementChange> EnablementChanges
)
{
    public bool CanApply =>
        Sections.All(static section =>
            section.Issues.All(static issue => !issue.BlocksApply)
            && section.Conflicts.All(static conflict => conflict.AllowedResolutions.Count == 0)
        );
}

public sealed record ConfigurationEnablementChange(
    HostFeatureFlags Feature,
    bool CurrentEnabled,
    bool ImportedEnabled,
    bool Selected
);

public sealed record ConfigurationImportActor(string TwitchUserId, string Login);

public sealed record ConfigurationImportApplied(
    Guid OperationId,
    Guid? ActivationId,
    IReadOnlyList<ConfigurationSectionId> ChangedSections
)
{
    public IReadOnlyList<ConfigurationPostCommitFailure> PostCommitFailures { get; init; } = [];

    public IReadOnlyList<ConfigurationImportManualFollowUp> ManualFollowUps { get; init; } = [];
}

public sealed record AutomationImportedEffects(
    AutomationFlowId FlowId,
    string Name,
    bool IsEnabled,
    bool? PreviouslyEnabled,
    AutomationEffectDisclosure Effects
);

public sealed class AutomationImportReview
{
    internal AutomationImportReview(
        int hostId,
        IReadOnlyList<AutomationImportedEffects> flows,
        System.Collections.Immutable.ImmutableArray<AutomationEffectParent> parents
    )
    {
        HostId = hostId;
        Flows = flows;
        Parents = parents;
    }

    internal int HostId { get; }
    public IReadOnlyList<AutomationImportedEffects> Flows { get; }
    public System.Collections.Immutable.ImmutableArray<AutomationEffectParent> Parents { get; }
    public IEnumerable<AutomationEffectParent> DisabledParents =>
        Parents.Where(parent =>
            parent.WillDisable && Flows.All(flow => flow.FlowId != parent.FlowId)
        );
    public bool RequiresApproval =>
        Flows.Any(flow => flow.IsEnabled && flow.Effects.Summary?.IsEmpty is not true);

    internal static bool Same(AutomationImportReview? left, AutomationImportReview? right) =>
        left is not null
        && right is not null
        && left.HostId == right.HostId
        && left.Flows.Count == right.Flows.Count
        && left.Flows.All(flow =>
            right.Flows.Any(current =>
                current.FlowId == flow.FlowId
                && current.IsEnabled == flow.IsEnabled
                && current.PreviouslyEnabled == flow.PreviouslyEnabled
                && AutomationFlowService.SameEffects(current.Effects, flow.Effects)
            )
        )
        && AutomationFlowService.SameEffectParents(left.Parents, right.Parents);
}
