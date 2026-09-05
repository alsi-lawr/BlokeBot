using BlokeBot.Core.Features.ViewerPortal;

namespace BlokeBot.Core.Hosting;

public static partial class BlokeBotFeatureServiceCollectionExtensions
{
    public static IServiceCollection AddBlokeBotViewerPortal(this IServiceCollection services)
    {
        _ = ViewerPortalCatalogue.Descriptors;
        _ = services.AddSingleton<ViewerPortalAccess>();
        _ = services.AddSingleton<PortalActivityProjectors>();
        _ = services.AddSingleton<PortalDirectoryProjectors>();
        _ = services.AddSingleton<PortalPersonalProjectors>();
        _ = services.AddSingleton<PortalProjectors>();
        _ = services.AddSingleton<ViewerPortalCatalogueService>();
        _ = services.AddSingleton<PortalPersonalReader>();
        _ = services.AddScoped<PortalCircuitConnection>();
        _ = services.AddScoped<Microsoft.AspNetCore.Components.Server.Circuits.CircuitHandler>(
            provider => provider.GetRequiredService<PortalCircuitConnection>()
        );
        return services;
    }
}
