using System.Collections.Immutable;

namespace BlokeBot.Core.Features.Automations;

public readonly record struct AutomationFlowId(Guid Value);

public readonly record struct AutomationNodeId(Guid Value);

public readonly record struct AutomationRunId(Guid Value);

public readonly record struct AutomationCanvasCoordinate(int Value);

public readonly record struct AutomationCanvasPosition(
    AutomationCanvasCoordinate X,
    AutomationCanvasCoordinate Y
);

public enum AutomationFlowOrientation
{
    Horizontal,
    Vertical,
}

public enum AutomationEdgeStyle
{
    Angular,
    Smooth,
}

public readonly record struct AutomationFlowCanvasSettings(
    AutomationFlowOrientation Orientation,
    AutomationEdgeStyle EdgeStyle
);

public readonly record struct AutomationExpressionLanguageVersion(int Value);

public static class AutomationExpressionLanguage
{
    public static AutomationExpressionLanguageVersion CurrentVersion { get; } = new(1);
}

public static class AutomationFlowSchema
{
    public const int CurrentVersion = 1;

    public const int NodeDisplayAliasMaximumLength = 200;
}

public static class AutomationContextSchema
{
    public const int CurrentVersion = 1;
}

public enum AutomationNodeFailurePolicy
{
    Stop,
    Continue,
}

public enum AutomationEdgeKind
{
    Flow,
    Data,
}

public enum AutomationInputBindingMode
{
    Fixed,
    Connected,
    Expression,
}

public sealed record AutomationExpressionSource(
    AutomationExpressionLanguageVersion LanguageVersion,
    string Source
);

public sealed record AutomationInputBinding(
    AutomationInputBindingMode Mode,
    AutomationExpressionSource? Expression
);

public sealed record AutomationFlowDraftNode(
    AutomationNodeId Id,
    PersistedAutomationNodeDefinition Definition,
    AutomationExpressionLanguageVersion ExpressionLanguageVersion,
    AutomationNodeFailurePolicy FailurePolicy,
    ImmutableDictionary<AutomationConfigurationFieldId, AutomationInputBinding> InputBindings,
    AutomationCanvasPosition Position = default,
    string? DisplayAlias = null
);

public sealed record AutomationFlowDraftEdge(
    Guid Id,
    AutomationEdgeKind Kind,
    AutomationNodeId SourceNodeId,
    AutomationPortId SourcePortId,
    AutomationNodeId TargetNodeId,
    AutomationPortId TargetPortId
);

public sealed record AutomationFlowDraft(
    AutomationFlowId? Id,
    AutomationHostId HostId,
    string Name,
    int SchemaVersion,
    bool IsEnabled,
    ImmutableArray<AutomationFlowDraftNode> Nodes,
    ImmutableArray<AutomationFlowDraftEdge> Edges,
    AutomationFlowCanvasSettings Canvas = default
);

public sealed record AutomationGraphError(
    AutomationNodeId? NodeId,
    string Code,
    string Message,
    AutomationConfigurationFieldId? FieldId = null
);

public abstract record AutomationFlowValidationOutcome
{
    private AutomationFlowValidationOutcome() { }

    public sealed record Valid : AutomationFlowValidationOutcome;

    public sealed record Invalid(ImmutableArray<AutomationGraphError> Errors)
        : AutomationFlowValidationOutcome;

    public sealed record FeatureDisabled : AutomationFlowValidationOutcome;

    public sealed record HostNotFound : AutomationFlowValidationOutcome;
}

public sealed record AutomationFlowSnapshot(
    AutomationFlowDraft Draft,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc
);

public abstract record AutomationFlowQueryOutcome
{
    private AutomationFlowQueryOutcome() { }

    public sealed record Available(ImmutableArray<AutomationFlowSnapshot> Flows)
        : AutomationFlowQueryOutcome;

    public sealed record Invalid(
        AutomationFlowId FlowId,
        ImmutableArray<AutomationGraphError> Errors
    ) : AutomationFlowQueryOutcome;

    public sealed record FeatureDisabled : AutomationFlowQueryOutcome;

    public sealed record HostNotFound : AutomationFlowQueryOutcome;
}

public abstract record AutomationFlowDeleteOutcome
{
    private AutomationFlowDeleteOutcome() { }

    public sealed record Deleted : AutomationFlowDeleteOutcome;

    public sealed record FeatureDisabled : AutomationFlowDeleteOutcome;

    public sealed record HostNotFound : AutomationFlowDeleteOutcome;

    public sealed record FlowNotFound : AutomationFlowDeleteOutcome;
}

public abstract record AutomationFlowDuplicateOutcome
{
    private AutomationFlowDuplicateOutcome() { }

    public sealed record Duplicated(AutomationFlowId FlowId) : AutomationFlowDuplicateOutcome;

    public sealed record Invalid(ImmutableArray<AutomationGraphError> Errors)
        : AutomationFlowDuplicateOutcome;

    public sealed record FeatureDisabled : AutomationFlowDuplicateOutcome;

    public sealed record HostNotFound : AutomationFlowDuplicateOutcome;

    public sealed record FlowNotFound : AutomationFlowDuplicateOutcome;
}

public sealed record AutomationSampleNodeOutcome(
    AutomationNodeId NodeId,
    AutomationNodeRunState State,
    string OutcomeCode
);

public abstract record AutomationSampleRunOutcome
{
    private AutomationSampleRunOutcome() { }

    public sealed record Completed(ImmutableArray<AutomationSampleNodeOutcome> Nodes)
        : AutomationSampleRunOutcome;

    public sealed record Failed(ImmutableArray<AutomationSampleNodeOutcome> Nodes)
        : AutomationSampleRunOutcome;

    public sealed record Invalid(ImmutableArray<AutomationGraphError> Errors)
        : AutomationSampleRunOutcome;

    public sealed record FeatureDisabled : AutomationSampleRunOutcome;

    public sealed record HostNotFound : AutomationSampleRunOutcome;
}

public abstract record AutomationFlowSaveOutcome
{
    private AutomationFlowSaveOutcome() { }

    public sealed record Saved(AutomationFlowId FlowId) : AutomationFlowSaveOutcome;

    public sealed record Invalid(ImmutableArray<AutomationGraphError> Errors)
        : AutomationFlowSaveOutcome;

    public sealed record FeatureDisabled : AutomationFlowSaveOutcome;

    public sealed record HostNotFound : AutomationFlowSaveOutcome;

    public sealed record FlowNotFound : AutomationFlowSaveOutcome;
}

public abstract record AutomationFlowEnableOutcome
{
    private AutomationFlowEnableOutcome() { }

    public sealed record Updated : AutomationFlowEnableOutcome;

    public sealed record Invalid(ImmutableArray<AutomationGraphError> Errors)
        : AutomationFlowEnableOutcome;

    public sealed record FeatureDisabled : AutomationFlowEnableOutcome;

    public sealed record HostNotFound : AutomationFlowEnableOutcome;

    public sealed record FlowNotFound : AutomationFlowEnableOutcome;
}

public sealed record AutomationTrigger(
    AutomationContext Context,
    AutomationConfiguration SourceConfiguration
);

public enum AutomationDispatchStatus
{
    Accepted,
    Duplicate,
    NoMatchingFlow,
    InvalidFlow,
    FeatureDisabled,
    HostNotFound,
}

public sealed record AutomationDispatchOutcome(
    AutomationDispatchStatus Status,
    ImmutableArray<AutomationRunId> RunIds
);

public enum AutomationResumeStatus
{
    Completed,
    Waiting,
    Failed,
    Invalidated,
    FeatureDisabled,
    NotFound,
}

public sealed record AutomationResumeOutcome(AutomationResumeStatus Status);

/// <summary>
/// Observes automation flow runs reaching a terminal <see cref="AutomationResumeStatus.Completed"/>
/// or <see cref="AutomationResumeStatus.Failed"/> outcome. The runtime may report the same terminal
/// run more than once; implementations must be idempotent and must not throw. Invalidated and
/// feature-disabled outcomes are never reported, so observers cause no effects for suppressed work.
/// </summary>
public interface IAutomationRunCompletionObserver
{
    Task RunFinishedAsync(
        AutomationRunId runId,
        AutomationResumeStatus status,
        CancellationToken cancellationToken
    );
}

public sealed record AutomationRunSummary(
    AutomationRunId Id,
    AutomationFlowId FlowId,
    AutomationFlowRunState State,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    ImmutableArray<AutomationNodeRunSummary> Nodes
)
{
    public AutomationNodeRunSummary? FailedNode =>
        Nodes.FirstOrDefault(static node =>
            node.State
                is AutomationNodeRunState.Failed
                    or AutomationNodeRunState.ContinuedAfterFailure
        );
}

public enum AutomationFlowRunState
{
    Running,
    Waiting,
    Completed,
    Failed,
    Invalidated,
}

public sealed record AutomationNodeRunSummary(
    AutomationNodeId NodeId,
    AutomationNodeRunState State,
    string? OutcomeCode,
    DateTimeOffset? CompletedAtUtc
);

public enum AutomationNodeRunState
{
    Pending,
    Running,
    Succeeded,
    Failed,
    ContinuedAfterFailure,
    Invalidated,
}

public abstract record AutomationRunQueryOutcome
{
    private AutomationRunQueryOutcome() { }

    public sealed record Available(ImmutableArray<AutomationRunSummary> Runs)
        : AutomationRunQueryOutcome;

    public sealed record FeatureDisabled : AutomationRunQueryOutcome;

    public sealed record HostNotFound : AutomationRunQueryOutcome;
}
