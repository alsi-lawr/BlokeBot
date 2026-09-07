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
    AutomationSubflowRevisionId? RevisionId,
    AutomationNodeId NodeId
);

public abstract record AutomationSubflowPublishOutcome
{
    private AutomationSubflowPublishOutcome() { }

    public sealed record Published(
        AutomationSubflowRevision Revision,
        ImmutableArray<AutomationSubflowCaller> IncompatibleCallers
    ) : AutomationSubflowPublishOutcome;

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
    AutomationSubflowRevisionId Id,
    AutomationSubflowId SubflowId,
    int Revision,
    string Name,
    string Description
);

public sealed record AutomationSubflowLibraryPage(
    ImmutableArray<AutomationSubflowLibrarySummary> Revisions,
    int? NextOffset
);

public abstract record AutomationSubflowPreviewOutcome
{
    private AutomationSubflowPreviewOutcome() { }

    public sealed record Ready(
        ImmutableArray<AutomationSubflowCaller> IncompatibleCallers,
        AutomationSubflowRevision Candidate
    ) : AutomationSubflowPreviewOutcome;

    public sealed record Invalid(ImmutableArray<AutomationGraphError> Errors)
        : AutomationSubflowPreviewOutcome;
}
