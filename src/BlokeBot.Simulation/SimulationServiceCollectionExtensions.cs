using BlokeBot.Core.Features.HostedChannels.Status;
using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Core.Features.TwitchOperations;
using BlokeBot.Core.Features.TwitchOperations.Shoutouts.AutomaticRaids;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BlokeBot.Simulation;

internal static class SimulationServiceCollectionExtensions
{
    public static IServiceCollection AddBlokeBotSimulation(this IServiceCollection services)
    {
        _ = services.Replace(
            ServiceDescriptor.Singleton<TimeProvider>(new SimulationTimeProvider())
        );
        _ = services.AddSingleton<SimulationCommandCatalogScenario>();
        _ = services.Replace(
            ServiceDescriptor.Singleton<IHostStreamLivenessProvider>(static provider =>
                provider.GetRequiredService<SimulationCommandCatalogScenario>()
            )
        );
        _ = services.Replace(
            ServiceDescriptor.Singleton<IPointTargetUserLookup, SimulationPointTargetUserLookup>()
        );
        _ = services.Replace(
            ServiceDescriptor.Singleton<
                IAutomaticRaidShoutoutDelivery,
                SimulationAutomaticRaidShoutoutDelivery
            >()
        );
        _ = services.AddSingleton<SimulationNativeTwitchDashboardOperations>();
        _ = services.Replace(
            ServiceDescriptor.Singleton<IShoutoutDashboardOperations>(static provider =>
                provider.GetRequiredService<SimulationNativeTwitchDashboardOperations>()
            )
        );
        _ = services.Replace(
            ServiceDescriptor.Singleton<IPollDashboardOperations>(static provider =>
                provider.GetRequiredService<SimulationNativeTwitchDashboardOperations>()
            )
        );
        _ = services.Replace(
            ServiceDescriptor.Singleton<IClipMarkerDashboardOperations>(static provider =>
                provider.GetRequiredService<SimulationNativeTwitchDashboardOperations>()
            )
        );
        _ = services.Replace(
            ServiceDescriptor.Singleton<IChannelPointsDashboardOperations>(static provider =>
                provider.GetRequiredService<SimulationNativeTwitchDashboardOperations>()
            )
        );
        _ = services.Replace(
            ServiceDescriptor.Singleton<IPredictionDashboardOperations>(static provider =>
                provider.GetRequiredService<SimulationNativeTwitchDashboardOperations>()
            )
        );
        _ = services.AddSingleton<SimulationFixtureSeeder>();
        _ = services.AddSingleton<SimulationReadiness>();
        _ = services.AddSingleton<SimulationStartupCoordinator>();
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
