using BlokeBot.Core.Features.Dashboards;

namespace BlokeBot.Core.Hosting;

public static partial class BlokeBotFeatureServiceCollectionExtensions
{
    public static IServiceCollection AddBlokeBotDashboards(this IServiceCollection services)
    {
        _ = services.AddSingleton<DashboardAuthority>();
        _ = services.AddSingleton<DashboardConfigurationService>();
        _ = services.AddSingleton<DashboardBindingReader>();
        _ = services.AddSingleton<DashboardAutomationControls>();
        return services;
    }
}
