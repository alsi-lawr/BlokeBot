using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Features;
using BlokeBot.Plugins.Runtime;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BlokeBot.Core.Features.Plugins;

public static class PluginFeatureServiceCollectionExtensions
{
    public static IServiceCollection AddBlokeBotPluginFeatures(this IServiceCollection services)
    {
        services.TryAddSingleton<PluginDispatchSnapshotRegistry>();
        services.TryAddSingleton<IPluginDispatchSnapshotProvider>(provider =>
            provider.GetRequiredService<PluginDispatchSnapshotRegistry>()
        );
        services.TryAddSingleton<IPluginDispatchSnapshotSink>(provider =>
            provider.GetRequiredService<PluginDispatchSnapshotRegistry>()
        );
        services.TryAddSingleton<IPluginCommandActivationGate>(provider =>
            provider.GetRequiredService<PluginDispatchSnapshotRegistry>()
        );
        services.TryAddSingleton<PluginDispatchWorkRegistry>();
        services.TryAddSingleton<IPluginScheduleStore, PluginScheduleFileStore>();
        services.TryAddSingleton<PluginFeatureWorkCoordinator>();
        services.TryAddSingleton<IPluginFeatureWorkCoordinator>(provider =>
            provider.GetRequiredService<PluginFeatureWorkCoordinator>()
        );
        services.TryAddSingleton<
            IPluginRuntimeSnapshotProvider,
            EmptyPluginRuntimeSnapshotProvider
        >();
        services.TryAddSingleton<IPluginLifecycleSerialization, PluginLifecycleSerialization>();
        services.TryAddSingleton<
            IPluginLifecycleChangeNotifier,
            EmptyPluginLifecycleChangeNotifier
        >();
        services.TryAddSingleton<PluginFeatureDeclarationRegistry>();
        services.TryAddSingleton<IPluginFeatureDeclarationProvider>(provider =>
            provider.GetRequiredService<PluginFeatureDeclarationRegistry>()
        );
        services.TryAddSingleton<IPluginFeatureDeclarationPublisher>(provider =>
            provider.GetRequiredService<PluginFeatureDeclarationRegistry>()
        );
        services.TryAddSingleton<PluginFeatureSnapshotRegistry>();
        services.TryAddSingleton<IPluginFeatureSnapshotProvider>(provider =>
            provider.GetRequiredService<PluginFeatureSnapshotRegistry>()
        );
        services.TryAddSingleton<PluginSettingsValidator>();
        services.TryAddSingleton<PluginSettingValuesCodec>();
        services.TryAddSingleton<IPluginSecretProtector, DataProtectionPluginSecretProtector>();
        services.TryAddSingleton<IPluginHostContextResolver, PluginHostContextResolver>();
        services.TryAddSingleton<PluginFeatureEventSubReconciler>();
        services.TryAddSingleton<IPluginFeatureReconciler>(provider =>
            provider.GetRequiredService<PluginFeatureEventSubReconciler>()
        );
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IPluginHostModule, PluginDiagnosticsHostModule>()
        );
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IPluginHostModule, PluginResponsesHostModule>()
        );
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IPluginHostModule, PluginChatHostModule>()
        );
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IPluginHostModule, PluginOverlayHostModule>()
        );
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IPluginHostModule, PluginPointsHostModule>()
        );
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IPluginHostModule, PluginTwitchHostModule>()
        );
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IPluginHostModule, PluginSchedulesHostModule>()
        );
        services.TryAddSingleton<PluginHostModuleCatalog>();
        services.TryAddSingleton<IPluginCoreDependencyChecker>(provider =>
            provider.GetRequiredService<PluginHostModuleCatalog>()
        );
        services.TryAddSingleton<IPluginHostCallDispatcher>(provider =>
            provider.GetRequiredService<PluginHostModuleCatalog>()
        );
        services.TryAddSingleton<
            IPluginFeatureLifecycleHealth,
            RuntimePluginFeatureLifecycleHealth
        >();
        services.TryAddSingleton<PluginFeatureManager>();
        services.TryAddSingleton<PluginFeatureAdmissionService>();
        services.TryAddSingleton<IPluginDispatchInvoker, PluginDispatchInvoker>();
        services.TryAddSingleton<PluginTwitchEventBridge>();
        services.TryAddSingleton<IPluginTwitchEventObserver>(provider =>
            provider.GetRequiredService<PluginTwitchEventBridge>()
        );
        services.TryAddSingleton<PluginRawEventSubBridge>();
        services.TryAddSingleton<IEventSubRawObserver>(provider =>
            provider.GetRequiredService<PluginRawEventSubBridge>()
        );
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IEventSubRequirementSource,
                PluginEventSubRequirementSource
            >()
        );
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IEventSubExactRequirementSource,
                PluginEventSubRequirementSource
            >()
        );
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IPluginPurgeDataOwner, PluginFeaturePurgeOwner>()
        );
        _ = services.Replace(
            ServiceDescriptor.Singleton<
                IPluginPendingWorkCanceller,
                PluginFeaturePendingWorkCanceller
            >()
        );
        _ = services.AddHostedService<PluginFeatureSnapshotHydrationService>();
        _ = services.AddHostedService<PluginFeatureRecoveryService>();
        _ = services.AddHostedService<PluginScheduleWorker>();
        _ = services.AddHostedService<PluginBlokeBotEventBridge>();
        return services;
    }
}

internal sealed class PluginFeatureRecoveryService(
    PluginFeatureManager manager,
    IPluginFeatureDeclarationProvider declarations,
    IPluginLifecycleChangeNotifier lifecycle,
    ILogger<PluginFeatureRecoveryService> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var declarationVersion = declarations.CurrentVersion;
        var lifecycleVersion = lifecycle.CurrentVersion;
        var declarationWait = declarations
            .WaitForChangeAsync(declarationVersion, stoppingToken)
            .AsTask();
        var lifecycleWait = lifecycle.WaitForChangeAsync(lifecycleVersion, stoppingToken).AsTask();
        await RecoverAsync(stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            _ = await Task.WhenAny(declarationWait, lifecycleWait);
            if (declarationWait.IsCompleted)
            {
                declarationVersion = await declarationWait;
                declarationWait = declarations
                    .WaitForChangeAsync(declarationVersion, stoppingToken)
                    .AsTask();
            }
            if (lifecycleWait.IsCompleted)
            {
                lifecycleVersion = await lifecycleWait;
                lifecycleWait = lifecycle
                    .WaitForChangeAsync(lifecycleVersion, stoppingToken)
                    .AsTask();
            }
            await RecoverAsync(stoppingToken);
        }
    }

    private async Task RecoverAsync(CancellationToken cancellationToken)
    {
        foreach (var pluginId in declarations.Current.Declarations.Keys)
        {
            try
            {
                _ = await manager.SynchronizeDeclarationAsync(pluginId, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Plugin feature recovery failed for {PluginId}",
                    pluginId.Value
                );
            }
        }
    }
}

internal sealed class EmptyPluginLifecycleChangeNotifier : IPluginLifecycleChangeNotifier
{
    public PluginLifecycleChangeVersion CurrentVersion { get; } = new(0);

    public async ValueTask<PluginLifecycleChangeVersion> WaitForChangeAsync(
        PluginLifecycleChangeVersion observed,
        CancellationToken cancellationToken
    )
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return observed;
    }
}

internal sealed class PluginFeatureSnapshotHydrationService(
    IPluginFeatureStore store,
    PluginFeatureSnapshotRegistry snapshots,
    IPluginLifecycleSerialization lifecycleSerialization
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pluginIds = (await store.LoadFeatureStatesAsync(null, stoppingToken))
            .Select(static state => state.Key.PluginId)
            .Distinct()
            .ToArray();
        foreach (var pluginId in pluginIds)
        {
            await using var lease = await lifecycleSerialization.AcquireAsync(
                pluginId,
                stoppingToken
            );
            snapshots.Hydrate(await store.LoadFeatureStatesAsync(pluginId, stoppingToken));
        }
        snapshots.Hydrate([]);
    }
}
