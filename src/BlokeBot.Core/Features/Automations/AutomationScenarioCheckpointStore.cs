using System.Collections.Immutable;

namespace BlokeBot.Core.Features.Automations;

internal sealed class AutomationScenarioCheckpointStore(
    Func<
        AutomationRuntimeSerialization.PersistedNode,
        AutomationTraceEventKind,
        AutomationTraceOutcome,
        IReadOnlyDictionary<AutomationPortId, AutomationResolvedValue>?,
        CancellationToken,
        Task
    > recordEvent
) : IAutomationPureCheckpointStore
{
    private readonly Dictionary<
        Guid,
        ImmutableDictionary<AutomationPortId, AutomationResolvedValue>
    > _outputs = [];
    private readonly HashSet<Guid> _failed = [];
    private readonly HashSet<Guid> _running = [];

    public ValueTask<AutomationPureCheckpoint> ReadOrBeginAsync(
        AutomationRuntimeSerialization.PersistedNode node,
        CancellationToken cancellationToken
    ) =>
        _outputs.TryGetValue(node.Id, out var outputs)
            ? ValueTask.FromResult<AutomationPureCheckpoint>(
                new AutomationPureCheckpoint.Available(outputs)
            )
        : _failed.Contains(node.Id) || !_running.Add(node.Id)
            ? ValueTask.FromResult<AutomationPureCheckpoint>(new AutomationPureCheckpoint.Failed())
        : ValueTask.FromResult<AutomationPureCheckpoint>(new AutomationPureCheckpoint.Begin());

    public async ValueTask RecordInputsAsync(
        AutomationRuntimeSerialization.PersistedNode node,
        ImmutableDictionary<AutomationPortId, AutomationResolvedValue> inputs,
        CancellationToken cancellationToken
    )
    {
        await recordEvent(
            node,
            AutomationTraceEventKind.ResolvedInputs,
            AutomationTraceOutcome.None,
            inputs,
            cancellationToken
        );
        await recordEvent(
            node,
            AutomationTraceEventKind.Attempt,
            AutomationTraceOutcome.None,
            null,
            cancellationToken
        );
    }

    public async ValueTask<bool> CompleteAsync(
        AutomationRuntimeSerialization.PersistedNode node,
        ImmutableDictionary<AutomationPortId, AutomationResolvedValue> outputs,
        CancellationToken cancellationToken
    )
    {
        _ = _running.Remove(node.Id);
        _outputs.Add(node.Id, outputs);
        await recordEvent(
            node,
            AutomationTraceEventKind.Outputs,
            AutomationTraceOutcome.Succeeded,
            outputs,
            cancellationToken
        );
        return true;
    }

    public async ValueTask FailAsync(
        AutomationRuntimeSerialization.PersistedNode node,
        string code,
        CancellationToken cancellationToken
    )
    {
        _ = _running.Remove(node.Id);
        _ = _failed.Add(node.Id);
        await recordEvent(
            node,
            AutomationTraceEventKind.Outputs,
            AutomationTraceOutcome.Failed,
            null,
            cancellationToken
        );
    }
}
