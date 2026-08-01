using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Core.Features.TwitchOperations;
using BlokeBot.Core.Features.TwitchOperations.Shoutouts.AutomaticRaids;
using BlokeBot.Twitch.Runtime;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BlokeBot.Simulation;

internal static class SimulationServiceCollectionExtensions
{
    public static IServiceCollection AddBlokeBotSimulation(this IServiceCollection services)
    {
        services.Replace(ServiceDescriptor.Singleton<TimeProvider>(new SimulationTimeProvider()));
        services.AddSingleton<SimulationCommandCatalogScenario>();
        services.Replace(
            ServiceDescriptor.Singleton<IHostStreamLivenessProvider>(provider =>
                provider.GetRequiredService<SimulationCommandCatalogScenario>()
            )
        );
        services.Replace(
            ServiceDescriptor.Singleton<IPointTargetUserLookup, SimulationPointTargetUserLookup>()
        );
        services.Replace(
            ServiceDescriptor.Singleton<
                IAutomaticRaidShoutoutDelivery,
                SimulationAutomaticRaidShoutoutDelivery
            >()
        );
        services.AddSingleton<SimulationNativeTwitchDashboardOperations>();
        services.Replace(
            ServiceDescriptor.Singleton<IShoutoutDashboardOperations>(provider =>
                provider.GetRequiredService<SimulationNativeTwitchDashboardOperations>()
            )
        );
        services.Replace(
            ServiceDescriptor.Singleton<IPollDashboardOperations>(provider =>
                provider.GetRequiredService<SimulationNativeTwitchDashboardOperations>()
            )
        );
        services.Replace(
            ServiceDescriptor.Singleton<IClipMarkerDashboardOperations>(provider =>
                provider.GetRequiredService<SimulationNativeTwitchDashboardOperations>()
            )
        );
        services.Replace(
            ServiceDescriptor.Singleton<IChannelPointsDashboardOperations>(provider =>
                provider.GetRequiredService<SimulationNativeTwitchDashboardOperations>()
            )
        );
        services.Replace(
            ServiceDescriptor.Singleton<IPredictionDashboardOperations>(provider =>
                provider.GetRequiredService<SimulationNativeTwitchDashboardOperations>()
            )
        );
        services.AddSingleton<SimulationFixtureSeeder>();
        services.AddSingleton<SimulationReadiness>();
        services.AddSingleton<SimulationStartupCoordinator>();
        return services;
    }

    private sealed class SimulationAutomaticRaidShoutoutDelivery : IAutomaticRaidShoutoutDelivery
    {
        public Task<AutomaticRaidShoutoutDeliveryResult> DeliverAsync(
            AutomaticRaidShoutoutDeliveryRequest request,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult<AutomaticRaidShoutoutDeliveryResult>(
                new AutomaticRaidShoutoutDeliveryResult.Delivered()
            );
    }

    private sealed class SimulationTimeProvider : TimeProvider
    {
        private readonly long _startedAtTimestamp = TimeProvider.System.GetTimestamp();

        public override DateTimeOffset GetUtcNow() =>
            SimulationMode.Now + TimeProvider.System.GetElapsedTime(_startedAtTimestamp);

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period
        ) => TimeProvider.System.CreateTimer(callback, state, dueTime, period);
    }

    private sealed class SimulationPointTargetUserLookup : IPointTargetUserLookup
    {
        public Task<bool> ExistsAsync(string login, CancellationToken ct) =>
            Task.FromResult(!string.IsNullOrWhiteSpace(login));
    }
}
