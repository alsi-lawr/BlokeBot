using System.Collections.Immutable;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.Automations;

public readonly record struct AutomationSubflowId(Guid Value);

public readonly record struct AutomationSubflowRevisionId(Guid Value);

public sealed record AutomationSubflowInterface(
    ImmutableArray<AutomationPortMetadata> Inputs,
    ImmutableArray<AutomationPortMetadata> Outputs
);

public sealed record AutomationSubflowDraft(
    AutomationSubflowId Id,
    string Description,
    AutomationSubflowInterface Interface,
    AutomationFlowDraft Graph
);

public sealed record AutomationSubflowRevision(
    AutomationSubflowRevisionId Id,
    AutomationSubflowId SubflowId,
    int Revision,
    string Description,
    AutomationSubflowInterface Interface,
    AutomationFlowDraft Graph,
    ImmutableArray<AutomationSubflowNodeContract> NodeContracts,
    HostFeatureFlags RequiredFeatures,
    ImmutableArray<AutomationPluginProvenance> PluginDependencies,
    DateTimeOffset PublishedAtUtc
);

public sealed record AutomationSubflowCaller(
    AutomationFlowId? FlowId,
    AutomationSubflowId? SubflowId,
    AutomationNodeId NodeId
);

public abstract record AutomationSubflowPublishOutcome
{
    private AutomationSubflowPublishOutcome() { }

    public sealed record Published(
        AutomationSubflowRevision Revision,
        ImmutableArray<AutomationSubflowCaller> IncompatibleCallers,
        AutomationSubflowInterface? PreviousInterface
    ) : AutomationSubflowPublishOutcome
    {
        public ImmutableArray<AutomationEffectParent> DisabledParents { get; init; } = [];
    }

    public sealed record Stale(AutomationSubflowPreviewOutcome.Ready Refreshed)
        : AutomationSubflowPublishOutcome;

    public sealed record Invalid(ImmutableArray<AutomationGraphError> Errors)
        : AutomationSubflowPublishOutcome;
}

public enum AutomationSubflowRemovalOutcome
{
    Removed,
    NotFound,
    Referenced,
}

public sealed record AutomationSubflowClosure(ImmutableArray<AutomationSubflowRevision> Revisions);

public abstract record AutomationSubflowClosureOutcome
{
    private AutomationSubflowClosureOutcome() { }

    public sealed record Available(AutomationSubflowClosure Closure)
        : AutomationSubflowClosureOutcome;

    public sealed record Invalid(ImmutableArray<AutomationGraphError> Errors)
        : AutomationSubflowClosureOutcome;
}

public sealed record AutomationSubflowNodeContract(
    AutomationNodeId NodeId,
    AutomationNodeKind Kind,
    AutomationDisplayMetadata Display,
    ImmutableArray<AutomationPortMetadata> Inputs,
    ImmutableArray<AutomationPortMetadata> Outputs,
    AutomationActionCapabilities Capabilities,
    AutomationActionRetrySafety RetrySafety
);

public sealed record AutomationSubflowLibraryQuery(string Search, int Offset = 0);

public sealed record AutomationSubflowLibrarySummary(
    AutomationSubflowId Id,
    string Name,
    string Description
);

public sealed record AutomationSubflowLibraryPage(
    ImmutableArray<AutomationSubflowLibrarySummary> Subflows,
    int? NextOffset
);

public abstract record AutomationSubflowPreviewOutcome
{
    private AutomationSubflowPreviewOutcome() { }

    public sealed record Ready(
        ImmutableArray<AutomationSubflowCaller> IncompatibleCallers,
        AutomationSubflowRevision Candidate,
        AutomationSubflowInterface? PreviousInterface,
        AutomationSubflowReview Reviewed
    ) : AutomationSubflowPreviewOutcome;

    public sealed record Invalid(ImmutableArray<AutomationGraphError> Errors)
        : AutomationSubflowPreviewOutcome;
}

public enum AutomationSubflowPortDirection
{
    Input,
    Output,
}

public sealed record AutomationSubflowPortChange(
    AutomationSubflowPortDirection Direction,
    AutomationPortMetadata? Previous,
    AutomationPortMetadata? Current
)
{
    public AutomationPortId Id => (Current ?? Previous!).Id;
    public bool Compatible =>
        Previous is not null
        && Current is not null
        && AutomationSubflowDefinitions.Compatible(new([Previous], []), new([Current], []));
    public bool MetadataChanged =>
        Previous is not null
        && Current is not null
        && (Previous.Name != Current.Name || Previous.Description != Current.Description);
}

public sealed class AutomationSubflowReview
{
    internal AutomationSubflowReview(
        AutomationSubflowDraft draft,
        AutomationSubflowRevisionId? previousRevision,
        AutomationSubflowInterface? previousInterface,
        ImmutableDictionary<AutomationSubflowId, AutomationSubflowRevision> dependencies,
        ImmutableArray<AutomationSubflowNodeContract> nodeContracts,
        HostFeatureFlags requiredFeatures,
        ImmutableArray<AutomationPluginProvenance> plugins,
        ImmutableArray<AutomationSubflowCallerImpact> callers,
        AutomationEffectDisclosure effects,
        ImmutableArray<AutomationEffectParent> effectParents
    )
    {
        Draft = draft;
        PreviousRevision = previousRevision;
        PreviousInterface = previousInterface;
        Dependencies = dependencies;
        NodeContracts = nodeContracts;
        RequiredFeatures = requiredFeatures;
        Plugins = plugins;
        Callers = callers;
        Effects = effects;
        EffectParents = effectParents;
    }

    public AutomationEffectDisclosure Effects { get; }
    public ImmutableArray<AutomationEffectParent> EffectParents { get; }
    internal AutomationSubflowDraft Draft { get; }
    internal AutomationSubflowRevisionId? PreviousRevision { get; }
    internal AutomationSubflowInterface? PreviousInterface { get; }
    internal ImmutableDictionary<
        AutomationSubflowId,
        AutomationSubflowRevision
    > Dependencies { get; }
    internal ImmutableArray<AutomationSubflowNodeContract> NodeContracts { get; }
    internal HostFeatureFlags RequiredFeatures { get; }
    internal ImmutableArray<AutomationPluginProvenance> Plugins { get; }
    internal ImmutableArray<AutomationSubflowCallerImpact> Callers { get; }
}

internal sealed record AutomationSubflowCallerImpact(
    AutomationSubflowCaller Caller,
    AutomationSubflowInterface? Interface
);
