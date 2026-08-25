using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BlokeBot.Plugins.Runtime;

public static class PluginLifecycleServiceCollectionExtensions
{
    public static IServiceCollection AddBlokeBotPluginRuntime(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(PluginLifecycleOptions.Default);
        services.TryAddSingleton<PluginWorkerCoordinator>();
        services.TryAddSingleton<IPluginLifecycleMigrationRunner, PluginLifecycleMigrationRunner>();
        services.TryAddSingleton<PluginRuntimeSnapshotRegistry>();
        services.TryAddSingleton<IPluginRuntimeSnapshotProvider>(provider =>
            provider.GetRequiredService<PluginRuntimeSnapshotRegistry>()
        );
        services.TryAddSingleton<IPluginRuntimeInvoker>(provider =>
            provider.GetRequiredService<PluginRuntimeSnapshotRegistry>()
        );
        services.TryAddSingleton<IPluginLifecycleChangeNotifier>(provider =>
            provider.GetRequiredService<PluginRuntimeSnapshotRegistry>()
        );
        services.TryAddSingleton<PluginLifecycleSerialization>();
        services.TryAddSingleton<IPluginLifecycleSerialization>(provider =>
            provider.GetRequiredService<PluginLifecycleSerialization>()
        );
        services.TryAddSingleton<
            IPluginLifecyclePackageResolver,
            UnavailablePluginLifecyclePackageResolver
        >();
        services.TryAddSingleton<IPluginPendingWorkCanceller, EmptyPluginPendingWorkCanceller>();
        services.TryAddSingleton<IPluginLifecycleWorkerManager, PluginLifecycleWorkerManager>();
        services.TryAddSingleton(provider => new PluginLifecycleCoordinator(
            provider.GetRequiredService<IPluginLifecycleStore>(),
            provider.GetRequiredService<IPluginLifecyclePackageResolver>(),
            provider.GetServices<IPluginMigrationDataOwner>(),
            provider.GetServices<IPluginPurgeDataOwner>(),
            provider.GetRequiredService<IPluginPendingWorkCanceller>(),
            provider.GetRequiredService<IPluginLifecycleWorkerManager>(),
            provider.GetRequiredService<PluginRuntimeSnapshotRegistry>(),
            provider.GetRequiredService<PluginLifecycleSerialization>(),
            provider.GetRequiredService<PluginLifecycleOptions>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<PluginLifecycleCoordinator>>()
        ));
        services.TryAddSingleton<IPluginLifecycleCoordinator>(provider =>
            provider.GetRequiredService<PluginLifecycleCoordinator>()
        );
        _ = services.AddHostedService<PluginLifecycleRecoveryService>();
        return services;
    }
}

internal sealed class PluginLifecycleRecoveryService(IPluginLifecycleCoordinator coordinator)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) =>
        await coordinator.RecoverAsync(stoppingToken);
}
