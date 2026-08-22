using BlokeBot.Core.Auth.Moderation;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.CustomCommands;
using BlokeBot.Core.Features.Overlays;
using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BlokeBot.Core.Features.ConfigurationTransfer;

public static class ConfigurationTransferServiceCollectionExtensions
{
    public static IServiceCollection AddBlokeBotConfigurationTransfer(
        this IServiceCollection services
    )
    {
        _ = services.AddSingleton<ConfigurationDocumentCodec>();
        _ = services.AddSingleton<ConfigurationDocumentExporter>();
        _ = services.AddSingleton<CustomCommandConfigurationTransferAdapter>();
        var overlaysAvailable = services.Any(value =>
            value.ServiceType == typeof(IOverlayAccessKeyGenerator)
        );
        var automationsAvailable = services.Any(value =>
            value.ServiceType == typeof(AutomationFlowService)
        );
        if (overlaysAvailable)
        {
            _ = services.AddSingleton<OverlayConfigurationTransferAdapter>();
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<
                    IConfigurationImportObserver,
                    OverlayConfigurationImportObserver
                >()
            );
        }
        if (automationsAvailable)
        {
            _ = services.AddSingleton<AutomationConfigurationTransferAdapter>();
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<
                    IConfigurationImportObserver,
                    AutomationConfigurationImportObserver
                >()
            );
        }
        _ = services.AddSingleton<ConfigurationImportObserverDispatcher>();
        _ = services.AddSingleton(provider => new ConfigurationImportPreviewService(
            provider.GetRequiredService<IDbContextFactory<BlokeBotDbContext>>(),
            overlaysAvailable
                ? provider.GetRequiredService<OverlayConfigurationTransferAdapter>()
                : UnavailableOverlayConfigurationTransferAdapter.Instance,
            automationsAvailable
                ? provider.GetRequiredService<AutomationConfigurationTransferAdapter>()
                : UnavailableAutomationConfigurationTransferAdapter.Instance
        ));
        _ = services.AddSingleton(provider => new ConfigurationTransferCoordinator(
            provider.GetRequiredService<IDbContextFactory<BlokeBotDbContext>>(),
            provider.GetRequiredService<CustomCommandConfigurationTransferAdapter>(),
            provider.GetRequiredService<IModeratorAuthorityService>(),
            provider.GetRequiredService<ConfigurationActivationQueue>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<ConfigurationTransferCoordinator>>(),
            provider.GetRequiredService<ConfigurationImportPreviewService>(),
            overlaysAvailable
                ? provider.GetRequiredService<OverlayConfigurationTransferAdapter>()
                : UnavailableOverlayConfigurationTransferAdapter.Instance,
            automationsAvailable
                ? provider.GetRequiredService<AutomationConfigurationTransferAdapter>()
                : UnavailableAutomationConfigurationTransferAdapter.Instance,
            provider.GetRequiredService<ConfigurationImportObserverDispatcher>(),
            overlaysAvailable
                ? provider.GetRequiredService<OverlayMediaMaintenanceService>().Gate
                : new SemaphoreSlim(1, 1)
        ));
        _ = services.AddSingleton<ConfigurationActivationQueue>();
        _ = services.AddSingleton<ConfigurationActivationDispatcher>();
        _ = services.AddSingleton<ConfigurationActivationService>();
        _ = services.AddHostedService<ConfigurationActivationWorker>();
        return services;
    }
}
