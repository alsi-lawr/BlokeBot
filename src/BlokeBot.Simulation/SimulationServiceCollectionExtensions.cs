using BlokeBot.Core.Features.Points.Balances;
using BlokeBot.Twitch.Runtime;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BlokeBot.Simulation;

internal static class SimulationServiceCollectionExtensions
{
    public static IServiceCollection AddBlokeBotSimulation(this IServiceCollection services)
    {
        services.Replace(ServiceDescriptor.Singleton<TimeProvider>(new SimulationTimeProvider()));
        services.Replace(
            ServiceDescriptor.Singleton<IPointTargetUserLookup, SimulationPointTargetUserLookup>()
        );
        services.AddSingleton<IPublicChatMessageSender, SimulationPublicChatMessageSender>();
        services.AddSingleton<SimulationFixtureSeeder>();
        return services;
    }

    private sealed class SimulationTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return SimulationMode.Now;
        }
    }

    private sealed class SimulationPointTargetUserLookup : IPointTargetUserLookup
    {
        public Task<bool> ExistsAsync(string login, CancellationToken ct)
        {
            return Task.FromResult(!string.IsNullOrWhiteSpace(login));
        }
    }

    private sealed class SimulationPublicChatMessageSender : IPublicChatMessageSender
    {
        public ValueTask<PublicChatSendOutcome> SendAsync(
            string channel,
            string message,
            PublicChatDeliveryDeadline deadline,
            CancellationToken cancellationToken
        )
        {
            return ValueTask.FromResult<PublicChatSendOutcome>(
                new PublicChatSendOutcome.Accepted()
            );
        }
    }
}
