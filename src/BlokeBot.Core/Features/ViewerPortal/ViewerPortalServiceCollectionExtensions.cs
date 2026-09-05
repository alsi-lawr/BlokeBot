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
        return services;
    }
}
