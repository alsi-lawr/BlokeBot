using BlokeBot.Plugins.Contracts;
using Microsoft.Extensions.Logging;

namespace BlokeBot.Plugins.Runtime;

public sealed record PluginLifecyclePackage(
    PluginInstallationIdentity Installation,
    PreparedPluginWorkerPackage PreparedPackage,
    string StateRoot,
    IPluginHostCallDispatcher HostCalls,
    ILogger<PluginWorkerClient> WorkerLogger
)
{
    public bool MatchesIdentity => PreparedPackage.Descriptor.Plugin == Installation;
}

public abstract record PluginLifecyclePackageResolution
{
    private PluginLifecyclePackageResolution() { }

    public sealed record Available(PluginLifecyclePackage Package)
        : PluginLifecyclePackageResolution;

    public sealed record Unavailable : PluginLifecyclePackageResolution;
}

public interface IPluginLifecyclePackageResolver
{
    ValueTask<PluginLifecyclePackageResolution> ResolveAsync(
        PluginInstallationIdentity installation,
        CancellationToken cancellationToken
    );
}

public enum PluginLifecycleOwnerFailureCode
{
    Rejected,
    Unavailable,
    Failed,
}

public abstract record PluginLifecycleOwnerOutcome
{
    private PluginLifecycleOwnerOutcome() { }

    public sealed record Succeeded : PluginLifecycleOwnerOutcome;

    public sealed record Failed(
        PluginLifecycleOwnerFailureCode Code,
        PluginLifecycleSafeDetail? Detail
    ) : PluginLifecycleOwnerOutcome;
}

public sealed record PluginMigrationContext(
    PluginInstallationIdentity Installation,
    PluginLifecycleFence Fence
);

public interface IPluginMigrationDataOwner
{
    ValueTask<PluginLifecycleOwnerOutcome> MigrateAsync(
        PluginMigrationContext context,
        CancellationToken cancellationToken
    );
}

public sealed record PluginPurgeContext(PluginId PluginId, PluginLifecycleFence Fence);

public interface IPluginPurgeDataOwner
{
    ValueTask<PluginLifecycleOwnerOutcome> PurgeAsync(
        PluginPurgeContext context,
        CancellationToken cancellationToken
    );
}

public interface IPluginPendingWorkCanceller
{
    ValueTask<PluginLifecycleOwnerOutcome> CancelAsync(
        PluginId pluginId,
        PluginLifecycleFence fence,
        CancellationToken cancellationToken
    );
}

internal sealed class UnavailablePluginLifecyclePackageResolver : IPluginLifecyclePackageResolver
{
    public ValueTask<PluginLifecyclePackageResolution> ResolveAsync(
        PluginInstallationIdentity installation,
        CancellationToken cancellationToken
    ) =>
        ValueTask.FromResult<PluginLifecyclePackageResolution>(
            new PluginLifecyclePackageResolution.Unavailable()
        );
}

internal sealed class EmptyPluginPendingWorkCanceller : IPluginPendingWorkCanceller
{
    public ValueTask<PluginLifecycleOwnerOutcome> CancelAsync(
        PluginId pluginId,
        PluginLifecycleFence fence,
        CancellationToken cancellationToken
    ) =>
        ValueTask.FromResult<PluginLifecycleOwnerOutcome>(
            new PluginLifecycleOwnerOutcome.Succeeded()
        );
}
