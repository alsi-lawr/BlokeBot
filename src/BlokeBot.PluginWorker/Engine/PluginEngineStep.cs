using BlokeBot.Plugins.Contracts;

namespace BlokeBot.PluginWorker;

internal abstract record PluginEngineStep
{
    private PluginEngineStep() { }

    internal sealed record HostCall(PluginHostCall Call) : PluginEngineStep;

    internal sealed record Completed(
        PluginWorkerInvocationOutcome Outcome,
        PluginWorkerInvocationMetrics Metrics
    ) : PluginEngineStep;

    internal sealed record Cancelled(
        PluginCancellationReason Reason,
        PluginWorkerInvocationMetrics Metrics
    ) : PluginEngineStep;
}
