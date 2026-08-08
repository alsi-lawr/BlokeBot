using BlokeBot.Core.Features.HostedChannels;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BlokeBot.Core.Features.Automations;

public static class AutomationServiceCollectionExtensions
{
    public static IServiceCollection AddBlokeBotAutomations(this IServiceCollection services)
    {
        _ = services.AddAutomationCatalogModule<CoreAutomationCatalogModule>();
        _ = services.AddAutomationCatalogModule<TwitchEventAutomationCatalogModule>();
        _ = services.AddAutomationCatalogModule<NativeOperationAutomationCatalogModule>();
        services.TryAddSingleton<AutomationDefinitionCatalog>();
        services.TryAddSingleton(static serviceProvider => new AutomationCatalogService(
            serviceProvider.GetRequiredService<AutomationDefinitionCatalog>(),
            serviceProvider.GetRequiredService<HostFeatureService>()
        ));
        services.TryAddSingleton<AutomationExpressionService>();
        services.TryAddSingleton<AutomationActionExecutor>();
        services.TryAddSingleton<AutomationFlowService>();
        services.TryAddSingleton<AutomationRuntimeService>();
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
                IHostFeatureChangeObserver,
                AutomationFeatureDisableObserver
            >()
        );
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IHostFeatureChangeObserver,
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

    public static IServiceCollection AddAutomationCatalogModule<TModule>(
        this IServiceCollection services
    )
        where TModule : class, IAutomationCatalogModule
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAutomationCatalogModule, TModule>());
        return services;
    }
}
