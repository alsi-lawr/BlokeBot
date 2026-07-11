using Microsoft.Extensions.Hosting;

namespace BlokeBot.Twitch.Runtime;

internal sealed class TwitchBotRuntimeHostedService : BackgroundService
{
    private readonly ITwitchBotRuntimeStrategy strategy;

    public TwitchBotRuntimeHostedService(
        TwitchBotSettings settings,
        IEnumerable<ITwitchBotRuntimeStrategy> strategies
    )
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(strategies);

        var matches = strategies
            .Where(candidate => candidate.Runtime == settings.Runtime)
            .Take(2)
            .ToArray();
        strategy = matches switch
        {
            [var selected] => selected,
            [] => throw new InvalidOperationException(
                $"No runtime strategy is registered for '{settings.Runtime}'."
            ),
            _ => throw new InvalidOperationException(
                $"Multiple runtime strategies are registered for '{settings.Runtime}'."
            ),
        };
    }

    internal Task RunSelectedStrategyAsync(CancellationToken cancellationToken) =>
        strategy.RunAsync(cancellationToken);

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        RunSelectedStrategyAsync(stoppingToken);
}
