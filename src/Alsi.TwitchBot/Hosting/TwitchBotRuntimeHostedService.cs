using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Alsi.TwitchBot;

internal sealed class TwitchBotRuntimeHostedService(
    IOptions<TwitchBotOptions> options,
    IServiceProvider services
) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return options.Value.Runtime switch
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
