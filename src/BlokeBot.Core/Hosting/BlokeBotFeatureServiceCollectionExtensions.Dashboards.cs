using BlokeBot.Core.Features.Dashboards;
using BlokeBot.Core.Features.Points.Giveaways;
using BlokeBot.Eventing;

namespace BlokeBot.Core.Hosting;

public static partial class BlokeBotFeatureServiceCollectionExtensions
{
    public static IServiceCollection AddBlokeBotDashboards(this IServiceCollection services)
    {
        _ = services.AddSingleton<DashboardGuessingReplies>();
        _ = services.AddSingleton<DashboardTargetCatalog>();
        _ = services.AddSingleton<DashboardAuthority>();
        _ = services.AddSingleton<DashboardConfigurationService>();
        _ = services.AddSingleton<DashboardBindingReader>();
        _ = services.AddSingleton<DashboardAutomationControls>();
        _ = services.AddSingleton<DashboardValueReader>();
        _ = services.AddEventBus<DashboardRecentViewersChanged>(
            ObserverBoundary.Named("BlokeBot.DashboardRecentViewers"),
            static change =>
                ObserverEventIdentity.Named($"BlokeBot.DashboardRecentViewers.{change.Host.Value}")
        );
        _ = services.AddSingleton<DashboardRecentViewers>();
        _ = services.AddSingleton<IChatMessageObserver>(provider =>
            provider.GetRequiredService<DashboardRecentViewers>()
        );
        _ = services.AddSingleton<DashboardPollControls>();
        _ = services.AddSingleton<DashboardPredictionControls>();
        _ = services.AddSingleton<DashboardGiveawayControls>();
        _ = services.AddSingleton<DashboardGuessingControls>();
        _ = services.AddEventBus<DashboardGiveawayChange>(
            ObserverBoundary.Named("BlokeBot.DashboardGiveaways"),
            static change =>
                ObserverEventIdentity.Named($"BlokeBot.DashboardGiveaways.{change.Host.Value}")
        );
        _ = services.AddSingleton<DashboardGiveawayChanges>();
        _ = services.AddSingleton<IPointsGiveawayChangeObserver>(provider =>
            provider.GetRequiredService<DashboardGiveawayChanges>()
        );
        return services;
    }
}
