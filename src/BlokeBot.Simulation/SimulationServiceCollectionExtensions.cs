using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BlokeBot.Simulation;

internal static class SimulationServiceCollectionExtensions
{
    public static IServiceCollection AddBlokeBotSimulation(this IServiceCollection services)
    {
        services.Replace(ServiceDescriptor.Singleton<TimeProvider>(new SimulationTimeProvider()));
        services.AddSingleton<SimulationFixtureSeeder>();
        services.AddSingleton<SimulationReadiness>();
        services.AddSingleton<SimulationStartupCoordinator>();
        return services;
    }

    private sealed class SimulationTimeProvider : TimeProvider
    {
        private readonly long _startedAtTimestamp = TimeProvider.System.GetTimestamp();

        public override DateTimeOffset GetUtcNow()
        {
            return SimulationMode.Now + TimeProvider.System.GetElapsedTime(_startedAtTimestamp);
        }

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period
        )
        {
            return TimeProvider.System.CreateTimer(callback, state, dueTime, period);
        }
    }
}
