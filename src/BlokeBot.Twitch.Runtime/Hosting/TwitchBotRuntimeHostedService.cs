using Microsoft.Extensions.Hosting;

namespace BlokeBot.Twitch.Runtime;

internal sealed class TwitchBotRuntimeHostedService(
    TwitchBotSettings settings,
    IServiceProvider services
) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return settings.Runtime switch
        {
            var runtime when Matches<TwitchEventSubRuntimeStrategy>(runtime) =>
                RunAsync<TwitchEventSubRuntimeStrategy>(stoppingToken),
            _ => RunAsync<TwitchIrcRuntimeStrategy>(stoppingToken),
        };
    }

    private static bool Matches<TStrategy>(TwitchBotRuntime runtime)
        where TStrategy : ITwitchBotRuntimeStrategy<TStrategy> => TStrategy.Matches(runtime);

    private Task RunAsync<TStrategy>(CancellationToken stoppingToken)
        where TStrategy : ITwitchBotRuntimeStrategy<TStrategy> =>
        TStrategy.RunAsync(services, stoppingToken);
}
