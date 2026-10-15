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
    ImmutableArray<AutomationTraceValue> Values,
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    )]
        AutomationDiagnostic? Diagnostic = null
);

public sealed record AutomationTraceEntry(int Sequence, AutomationTraceEventData Event);

public sealed record AutomationTraceOutcomeSummary(
    AutomationTraceOutcome? TerminalOutcome,
    bool? ObservedFailures
)
{
    internal static bool ObservesFailure(AutomationTraceEventData data) =>
        data.Node is not null
        && data.Kind
            is AutomationTraceEventKind.Result
                or AutomationTraceEventKind.Outputs
                or AutomationTraceEventKind.SubflowExit
        && data.Outcome
            is AutomationTraceOutcome.Failed
                or AutomationTraceOutcome.ContinuedAfterFailure
                or AutomationTraceOutcome.Invalidated
                or AutomationTraceOutcome.Interrupted;

    internal static AutomationTraceOutcome? RootTerminal(
        Guid traceId,
        AutomationTraceEventData data
    ) =>
        data.Kind == AutomationTraceEventKind.Terminal
        && data.Invocation.Id == traceId
        && data.Invocation.ParentId is null
        && data.Outcome != AutomationTraceOutcome.None
            ? data.Outcome
            : null;
}

public sealed record AutomationTraceSnapshot(
    AutomationTraceId Id,
    int SchemaVersion,
    AutomationFlowId? FlowId,
    AutomationRunId? ProductionRunId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    int ByteCount,
    AutomationTraceTruncation Truncation,
    ImmutableArray<AutomationTraceEntry> Events,
    AutomationTraceOutcomeSummary? OutcomeSummary = null
);

public sealed record AutomationTraceSummary(
    AutomationTraceId Id,
    AutomationFlowId? FlowId,
    AutomationRunId? ProductionRunId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    AutomationTraceTruncation Truncation,
    AutomationTraceOutcomeSummary? OutcomeSummary = null
);

public abstract record AutomationTraceReadOutcome
{
    private AutomationTraceReadOutcome() { }

    public sealed record Available(AutomationTraceSnapshot Trace) : AutomationTraceReadOutcome;

    public sealed record Expired : AutomationTraceReadOutcome;

    public sealed record NotFound : AutomationTraceReadOutcome;
}
