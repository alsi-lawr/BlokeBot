using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.HostedChannels.Authorization;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Persistence;
using BlokeBot.Plugins.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BlokeBot.Core.Features.Automations;

public static class AutomationServiceCollectionExtensions
{
    public static IServiceCollection AddBlokeBotAutomations(this IServiceCollection services)
    {
        services.TryAddSingleton<PluginAutomationCatalogRegistry>();
        services.TryAddSingleton<IPluginFeatureAutomationPlanner>(provider =>
            provider.GetRequiredService<PluginAutomationCatalogRegistry>()
        );
        services.TryAddSingleton<IPluginAutomationCatalogSink>(provider =>
            provider.GetRequiredService<PluginAutomationCatalogRegistry>()
        );
        _ = services.AddAutomationCatalogModule<CoreAutomationCatalogModule>();
        _ = services.AddAutomationCatalogModule<ExpandedAutomationCatalogModule>();
        services.TryAddSingleton<ExpandedAutomationRuntime>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IChatMessageObserver, AutomationIrcChatObserver>()
        );
        services.TryAddSingleton(p => new AutomationCountdownService(
            p.GetRequiredService<IDbContextFactory<BlokeBotDbContext>>(),
            p.GetRequiredService<TimeProvider>(),
            () => p.GetRequiredService<AutomationRuntimeService>()
        ));
        services.TryAddSingleton<AutomationManualRunService>();
        services.TryAddSingleton(p => new AutomationFeatureLifecycle(
            () => p.GetRequiredService<ExpandedAutomationRuntime>(),
            p.GetRequiredService<ILogger<AutomationFeatureLifecycle>>()
        ));
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IOverlayCueLifecycleObserver,
                AutomationCueLifecycleObserver
            >(p =>
                new(
                    p.GetRequiredService<IDbContextFactory<BlokeBotDbContext>>(),
                    () => p.GetRequiredService<AutomationRuntimeService>(),
                    p.GetRequiredService<TimeProvider>()
                )
            )
        );
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IHostFeatureActivationObserver,
                ExpandedAutomationActivationObserver
            >(p => new(() => p.GetRequiredService<ExpandedAutomationRuntime>()))
        );
        services.TryAddSingleton<IExpandedTwitchEventObserver>(p =>
            p.GetRequiredService<ExpandedAutomationRuntime>()
        );
        _ = services.AddSingleton<IEventSubExactRequirementSource>(p =>
            p.GetRequiredService<ExpandedAutomationRuntime>()
        );
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, ExpandedAutomationWorker>()
        );
        _ = services.AddAutomationCatalogModule<TwitchEventAutomationCatalogModule>();
        _ = services.AddAutomationCatalogModule<NativeOperationAutomationCatalogModule>();
        services.TryAddSingleton<AutomationDefinitionCatalog>();
        services.TryAddSingleton<PluginAutomationSourceAdmission>();
        services.TryAddSingleton<PluginAutomationRunCoordinator>();
        services.TryAddSingleton<IPluginAutomationSourceAdmission>(provider =>
            provider.GetRequiredService<PluginAutomationSourceAdmission>()
        );
        services.TryAddSingleton(static serviceProvider => new AutomationCatalogService(
            serviceProvider.GetRequiredService<AutomationDefinitionCatalog>(),
            serviceProvider.GetRequiredService<HostFeatureService>(),
            serviceProvider.GetRequiredService<AutomationExpressionService>(),
            serviceProvider.GetServices<IAutomationPureNodeHandler>(),
            serviceProvider.GetRequiredService<IAutomationIntegerEntropy>(),
            PluginExecution(serviceProvider)
        ));
        services.TryAddSingleton<IAutomationIntegerEntropy, AutomationProductionIntegerEntropy>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IAutomationPureNodeHandler, AutomationRandomNumberHandler>()
        );
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IAutomationPureNodeHandler, AutomationCelTransformHandler>()
        );
        services.TryAddSingleton<AutomationExpressionService>();
        services.TryAddSingleton<AutomationActionExecutor>();
        services.TryAddSingleton<AutomationFlowService>();
        services.TryAddSingleton<AutomationSubflowService>();
        services.TryAddSingleton<AutomationSubflowCurrentDataConversion>();
        services.TryAddSingleton<AutomationScenarioService>();
        services.TryAddSingleton<AutomationTraceStore>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, AutomationTraceCleanupWorker>()
        );
        services.TryAddSingleton(static provider =>
        {
            var runtime = new AutomationRuntimeService(
                provider.GetRequiredService<Microsoft.EntityFrameworkCore.IDbContextFactory<BlokeBot.Persistence.BlokeBotDbContext>>(),
                provider.GetRequiredService<AutomationCatalogService>(),
                provider.GetRequiredService<AutomationFlowService>(),
                provider.GetRequiredService<AutomationActionExecutor>(),
                provider.GetRequiredService<TimeProvider>(),
                provider.GetServices<IAutomationRunCompletionObserver>()
            );
            if (PluginExecution(provider) is { } pluginExecution)
            {
                runtime.UsePluginExecution(pluginExecution);
            }
            return runtime;
        });
        services.TryAddSingleton<IPluginAutomationRunDispatcher>(provider =>
            provider.GetRequiredService<AutomationRuntimeService>()
        );
        _ = services.Replace(
            ServiceDescriptor.Singleton<
                ICustomCommandAutomationRuntime,
                CustomCommandAutomationRuntime
            >()
        );
        services.TryAddSingleton<AutomationRunQueryService>();
        services.TryAddSingleton<TwitchEventAutomationRuntime>();
        services.TryAddSingleton(p => new TwitchEventSourceReadinessService(
            p.GetRequiredService<IDbContextFactory<BlokeBotDbContext>>(),
            p.GetRequiredService<AutomationCatalogService>(),
            p.GetRequiredService<AutomationRuntimeService>(),
            p.GetRequiredService<IHostBroadcasterTokenStatusProvider>(),
            p.GetRequiredService<ExpandedAutomationRuntime>()
        ));
        _ = services.AddSingleton<ITwitchEventAutomationObserver>(static serviceProvider =>
            serviceProvider.GetRequiredService<TwitchEventAutomationRuntime>()
        );
        services.TryAddSingleton<IAutomationEventSubRequirementSource>(static serviceProvider =>
            serviceProvider.GetRequiredService<TwitchEventAutomationRuntime>()
        );
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IAutomationRunCompletionObserver,
                RedemptionCompletionPolicyObserver
            >()
        );
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IHostFeatureActivationObserver,
                AutomationEventSubReconciliationObserver
            >()
        );
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, AutomationCatalogStartupService>()
        );
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, AutomationRuntimeWorker>()
        );
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, AutomationEventReceiptCleanupWorker>()
        );
        return services;
    }

    private static PluginAutomationExecutionService? PluginExecution(
        IServiceProvider serviceProvider
    ) =>
        serviceProvider.GetService<IPluginAutomationInvoker>() is { } invoker
            ? new(serviceProvider.GetRequiredService<AutomationDefinitionCatalog>(), invoker)
            : null;

    public static IServiceCollection AddAutomationCatalogModule<TModule>(
        this IServiceCollection services
    )
        where TModule : class, IAutomationCatalogModule
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAutomationCatalogModule, TModule>());
        return services;
    }
}

internal sealed class ExpandedAutomationActivationObserver(Func<ExpandedAutomationRuntime> runtime)
    : IHostFeatureActivationObserver
{
    public ValueTask<HostFeatureAutomaticWorkResult> ApplyAsync(
        HostFeatureActivationChange change,
        CancellationToken cancellationToken
    ) => runtime().ApplyAsync(change, cancellationToken);
}
