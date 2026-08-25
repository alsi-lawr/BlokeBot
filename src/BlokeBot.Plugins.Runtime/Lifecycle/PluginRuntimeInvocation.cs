using BlokeBot.Plugins.Contracts;

namespace BlokeBot.Plugins.Runtime;

public interface IPluginRuntimeInvoker
{
    ValueTask<PluginWorkerInvocationResult> InvokeAsync(
        PluginId pluginId,
        PluginLifecycleFence fence,
        PluginWorkerInvocationIdentity identity,
        PluginLiveInvocation invocation,
        CancellationToken cancellationToken
    );
}

public sealed partial class PluginRuntimeSnapshotRegistry : IPluginRuntimeInvoker
{
    public ValueTask<PluginWorkerInvocationResult> InvokeAsync(
        PluginId pluginId,
        PluginLifecycleFence fence,
        PluginWorkerInvocationIdentity identity,
        PluginLiveInvocation invocation,
        CancellationToken cancellationToken
    )
    {
        IPluginLifecycleWorkerSession? worker;
        lock (_sync)
        {
            worker =
                _current.Slots.TryGetValue(pluginId, out var slot)
                && slot.Entry.Fence == fence
                && slot.Entry.Phase == PluginLifecyclePhase.Active
                && slot.Entry.WorkerMode == PluginWorkerMode.Admitted
                    ? slot.Worker
                    : null;
        }

        return worker is null
            ? ValueTask.FromResult(Unavailable())
            : worker.InvokeAsync(identity, invocation, cancellationToken);
    }

    private static PluginWorkerInvocationResult Unavailable() =>
        new(
            new PluginWorkerInvocationOutcome.Failed(
                new(
                    PluginWorkerFailureCode.WorkerTerminated,
                    "The admitted plugin worker is unavailable."
                )
            ),
            PluginWorkerInvocationMetrics.Empty,
            []
        );
}
