using BlokeBot.Plugins.Contracts;
using Microsoft.Extensions.Logging;

namespace BlokeBot.Plugins.Runtime;

public sealed record PluginLifecyclePackage(
    PluginInstallationIdentity Installation,
    PluginPackageOperationId PackageOperationId,
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
        PluginPackageOperationId packageOperationId,
        CancellationToken cancellationToken
    );
}

public sealed record PluginLifecycleActivationContext(
    PluginInstallationIdentity Installation,
    PluginLifecycleFence Fence,
    PluginLifecyclePackage Package
);

public interface IPluginLifecycleActivationPublisher
{
    ValueTask<PluginLifecycleOwnerOutcome> PublishAsync(
        PluginLifecycleActivationContext context,
        CancellationToken cancellationToken
    );

    ValueTask WithdrawAsync(
        PluginLifecycleActivationContext context,
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
)
{
    public PluginLifecyclePackage? Package { get; init; }

    internal PluginMigrationContext(
        PluginInstallationIdentity installation,
        PluginLifecycleFence fence,
        PluginLifecyclePackage package
    )
        : this(installation, fence) => Package = package;
}

public interface IPluginMigrationDataOwner
{
    ValueTask<PluginLifecycleOwnerOutcome> MigrateAsync(
        PluginMigrationContext context,
        CancellationToken cancellationToken
    );
}

public sealed record PluginRemovalContext(PluginId PluginId, PluginLifecycleFence Fence);

public interface IPluginRemovalDataOwner
{
    ValueTask<PluginLifecycleOwnerOutcome> RemoveAsync(
        PluginRemovalContext context,
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
        PluginPackageOperationId packageOperationId,
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
