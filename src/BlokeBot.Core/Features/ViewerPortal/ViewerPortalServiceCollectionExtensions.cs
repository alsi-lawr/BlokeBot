using BlokeBot.Core.Features.ViewerPortal;

namespace BlokeBot.Core.Hosting;

public static partial class BlokeBotFeatureServiceCollectionExtensions
{
    public static IServiceCollection AddBlokeBotViewerPortal(this IServiceCollection services)
    {
        _ = services.AddSingleton<ViewerPortalAccess>();
        return services;
    }
}
