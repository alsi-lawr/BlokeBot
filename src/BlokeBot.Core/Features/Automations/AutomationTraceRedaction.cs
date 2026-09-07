namespace BlokeBot.Core.Features.Automations;

internal static class AutomationTraceRedaction
{
    internal static AutomationTraceEventData Event(
        Guid invocationId,
        AutomationTraceEventKind kind,
        DateTime now,
        AutomationRuntimeSerialization.PersistedNode? node = null,
        AutomationTraceOutcome outcome = AutomationTraceOutcome.None,
        string? port = null,
        DateTime? due = null,
        IReadOnlyDictionary<AutomationPortId, AutomationResolvedValue>? values = null
    ) =>
        new(
            kind,
            new(now, TimeSpan.Zero),
            node?.Invocation is { } nested
                ? AutomationFrozenSubflows.Trace(nested, invocationId)
                : new(invocationId),
            node is null
                ? null
                : new(
                    new(node.AuthorNodeId ?? node.Id),
                    new(node.DefinitionId),
                    new(node.DefinitionSchemaVersion),
                    node.ContinueOnFailure
                        ? AutomationNodeFailurePolicy.Continue
                        : AutomationNodeFailurePolicy.Stop
                ),
            outcome,
            outcome
                is AutomationTraceOutcome.Failed
                    or AutomationTraceOutcome.ContinuedAfterFailure
                    or AutomationTraceOutcome.Interrupted
                ? AutomationTraceRetry.NotRetried
                : AutomationTraceRetry.NotApplicable,
            port is null ? null : new(port),
            due is { } date ? new(date, TimeSpan.Zero) : null,
            values is null
                ? []
                :
                [
                    .. values
                        .OrderBy(pair => pair.Key.Value, StringComparer.Ordinal)
                        .Select(pair => new AutomationTraceValue(
                            pair.Key,
                            AutomationPureHandlerRegistry.ValueType(pair.Value.Value),
                            pair.Value.Provenance.IsDefault ? [] : pair.Value.Provenance,
                            pair.Value.SafeTriggerFields.IsDefault
                                ? []
                                : pair.Value.SafeTriggerFields
                        )),
                ]
        );
}
