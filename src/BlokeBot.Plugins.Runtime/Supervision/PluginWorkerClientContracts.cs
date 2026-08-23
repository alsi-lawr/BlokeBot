using BlokeBot.Plugins.Contracts;
using Microsoft.Extensions.Logging;

namespace BlokeBot.Plugins.Runtime;

public interface IPluginHostCallDispatcher
{
    ValueTask<PluginHostCallOutcome> DispatchAsync(
        PluginHostCall call,
        CancellationToken cancellationToken
    );
}

public sealed record PluginWorkerStartOptions(
    PreparedPluginWorkerPackage Package,
    string StateRoot,
    PluginWorkerMode Mode,
    IPluginHostCallDispatcher HostCalls,
    ILogger<PluginWorkerClient> Logger,
    PluginWorkerExecutable? Executable = null
);

public abstract record PluginWorkerStartOutcome
{
    private PluginWorkerStartOutcome() { }

    public sealed record Started(PluginWorkerClient Client) : PluginWorkerStartOutcome;

    public sealed record Rejected(PluginWorkerHandshakeFailure Failure) : PluginWorkerStartOutcome;

    public sealed record Failed(PluginWorkerFailure Failure) : PluginWorkerStartOutcome;
}

public sealed record PluginWorkerInvocationResult(
    PluginWorkerInvocationOutcome Outcome,
    PluginWorkerInvocationMetrics Metrics,
    IReadOnlyList<PluginWorkerDiagnostic> Diagnostics
);
