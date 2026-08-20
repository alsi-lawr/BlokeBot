using BlokeBot.Core.Features.CustomCommands;

namespace BlokeBot.Core.Features.ConfigurationTransfer;

public static class ConfigurationTransferServiceCollectionExtensions
{
    public static IServiceCollection AddBlokeBotConfigurationTransfer(
        this IServiceCollection services
    )
    {
        _ = services.AddSingleton<ConfigurationDocumentCodec>();
        _ = services.AddSingleton<ConfigurationDocumentExporter>();
        _ = services.AddSingleton<ConfigurationImportPreviewService>();
        _ = services.AddSingleton<CustomCommandConfigurationTransferAdapter>();
        _ = services.AddSingleton<ConfigurationTransferCoordinator>();
        _ = services.AddSingleton<ConfigurationActivationQueue>();
        _ = services.AddSingleton<ConfigurationActivationDispatcher>();
        _ = services.AddSingleton<ConfigurationActivationService>();
        _ = services.AddHostedService<ConfigurationActivationWorker>();
        return services;
    }
}
