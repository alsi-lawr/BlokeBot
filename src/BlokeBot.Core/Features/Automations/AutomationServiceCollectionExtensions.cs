using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Plugins.Features;
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
        services.TryAddSingleton<TwitchEventSourceReadinessService>();
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
