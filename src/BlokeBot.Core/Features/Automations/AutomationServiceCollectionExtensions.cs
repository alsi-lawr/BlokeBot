using BlokeBot.Core.Features.HostedChannels;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BlokeBot.Core.Features.Automations;

public static class AutomationServiceCollectionExtensions
{
    public static IServiceCollection AddBlokeBotAutomations(this IServiceCollection services)
    {
        _ = services.AddAutomationCatalogModule<CoreAutomationCatalogModule>();
        services.TryAddSingleton<AutomationDefinitionCatalog>();
        services.TryAddSingleton(static serviceProvider => new AutomationCatalogService(
            serviceProvider.GetRequiredService<AutomationDefinitionCatalog>(),
            serviceProvider.GetRequiredService<HostFeatureService>()
        ));
        services.TryAddSingleton<AutomationExpressionService>();
        services.TryAddSingleton<AutomationActionExecutor>();
        services.TryAddSingleton<AutomationFlowService>();
        services.TryAddSingleton<AutomationRuntimeService>();
        services.TryAddSingleton<AutomationRunQueryService>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IHostFeatureChangeObserver,
                AutomationFeatureDisableObserver
            >()
        );
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, AutomationCatalogStartupService>()
        );
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, AutomationRuntimeWorker>()
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
