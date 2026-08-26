using BlokeBot.Plugins.Contracts;

namespace BlokeBot.Plugins.Runtime;

public enum PluginLifecycleCommandRejectionCode
{
    NotFound,
    Busy,
    AlreadyActive,
    FaultedInstallation,
    NotFaulted,
    Conflict,
    InvalidPackageIdentity,
    GenerationExhausted,
}

public abstract record PluginLifecycleCommandOutcome
{
    private PluginLifecycleCommandOutcome() { }

    public sealed record Succeeded(PluginLifecycleView View) : PluginLifecycleCommandOutcome;

    public sealed record Failed(PluginLifecycleView View) : PluginLifecycleCommandOutcome;

    public sealed record Removed(PluginId PluginId) : PluginLifecycleCommandOutcome;

    public sealed record Rejected(
        PluginLifecycleCommandRejectionCode Code,
        PluginLifecycleView? Current
    ) : PluginLifecycleCommandOutcome;
}

public interface IPluginLifecycleCoordinator
{
    ValueTask<PluginLifecycleCommandOutcome> ActivateAsync(
        PluginLifecycleOperationId operationId,
        PluginLifecyclePackage package,
        CancellationToken cancellationToken
    );

    ValueTask<PluginLifecycleCommandOutcome> RemoveAsync(
        PluginId pluginId,
        PluginLifecycleOperationId operationId,
        CancellationToken cancellationToken
    );

    ValueTask<PluginLifecycleCommandOutcome> RestartAsync(
        PluginId pluginId,
        PluginLifecycleOperationId operationId,
        CancellationToken cancellationToken
    );

    ValueTask RecoverAsync(CancellationToken cancellationToken);
}
