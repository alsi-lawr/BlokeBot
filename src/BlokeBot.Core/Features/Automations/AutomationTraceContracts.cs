using System.Collections.Immutable;

namespace BlokeBot.Core.Features.Automations;

public readonly record struct AutomationTraceId(Guid Value);

public enum AutomationTraceEventKind
{
    Admission,
    Scheduled,
    ResolvedInputs,
    Outputs,
    Branch,
    Attempt,
    Result,
    Delay,
    Resume,
    Cancellation,
    Recovery,
    Terminal,
    SubflowEntry,
    SubflowExit,
}

public enum AutomationTraceOutcome
{
    None,
    Succeeded,
    Failed,
    ContinuedAfterFailure,
    Invalidated,
    Interrupted,
    Cancelled,
}

public enum AutomationTraceRetry
{
    NotApplicable,
    NotRetried,
}

public enum AutomationTraceTruncation
{
    None,
    EventLimit,
    ByteLimit,
}

public sealed record AutomationTraceInvocation(
    Guid Id,
    Guid? ParentId = null,
    Guid? SubflowId = null,
    Guid? RevisionId = null
);

public sealed record AutomationTraceNode(
    AutomationNodeId Id,
    AutomationDefinitionId DefinitionId,
    AutomationSchemaVersion SchemaVersion,
    AutomationNodeFailurePolicy FailurePolicy
);

public sealed record AutomationTraceValue(
    AutomationPortId PortId,
    AutomationPortValueType ValueType,
    ImmutableArray<AutomationValueProvenance> Provenance,
    ImmutableArray<AutomationSafeTriggerFieldId> SafeTriggerFields
);

public sealed record AutomationTraceEventData(
    AutomationTraceEventKind Kind,
    DateTimeOffset TimeUtc,
    AutomationTraceInvocation Invocation,
    AutomationTraceNode? Node,
    AutomationTraceOutcome Outcome,
    AutomationTraceRetry Retry,
    AutomationPortId? SelectedPort,
    DateTimeOffset? DueAtUtc,
    ImmutableArray<AutomationTraceValue> Values
);

public sealed record AutomationTraceEntry(int Sequence, AutomationTraceEventData Event);

public sealed record AutomationTraceSnapshot(
    AutomationTraceId Id,
    int SchemaVersion,
    AutomationFlowId? FlowId,
    AutomationRunId? ProductionRunId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    int ByteCount,
    AutomationTraceTruncation Truncation,
    ImmutableArray<AutomationTraceEntry> Events
);

public sealed record AutomationTraceSummary(
    AutomationTraceId Id,
    AutomationFlowId? FlowId,
    AutomationRunId? ProductionRunId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    AutomationTraceTruncation Truncation
);

public abstract record AutomationTraceReadOutcome
{
    private AutomationTraceReadOutcome() { }

    public sealed record Available(AutomationTraceSnapshot Trace) : AutomationTraceReadOutcome;

    public sealed record Expired : AutomationTraceReadOutcome;

    public sealed record NotFound : AutomationTraceReadOutcome;
}
