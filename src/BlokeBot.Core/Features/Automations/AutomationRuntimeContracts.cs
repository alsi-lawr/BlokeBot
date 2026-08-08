using System.Collections.Immutable;

namespace BlokeBot.Core.Features.Automations;

public readonly record struct AutomationFlowId(Guid Value);

public readonly record struct AutomationNodeId(Guid Value);

public readonly record struct AutomationRunId(Guid Value);

public readonly record struct AutomationExpressionLanguageVersion(int Value);

public static class AutomationExpressionLanguage
{
    public static AutomationExpressionLanguageVersion CurrentVersion { get; } = new(1);
}

public static class AutomationFlowSchema
{
    public const int CurrentVersion = 1;
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

public sealed record AutomationExpressionSource(
    AutomationExpressionLanguageVersion LanguageVersion,
    string Source
);

public sealed record AutomationFlowDraftNode(
    AutomationNodeId Id,
    PersistedAutomationNodeDefinition Definition,
    AutomationExpressionLanguageVersion ExpressionLanguageVersion,
    AutomationNodeFailurePolicy FailurePolicy,
    ImmutableDictionary<AutomationConfigurationFieldId, AutomationExpressionSource> FieldExpressions
);

public sealed record AutomationFlowDraftEdge(
    Guid Id,
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
    ImmutableArray<AutomationFlowDraftEdge> Edges
);

public sealed record AutomationGraphError(AutomationNodeId? NodeId, string Code, string Message);

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
);

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
