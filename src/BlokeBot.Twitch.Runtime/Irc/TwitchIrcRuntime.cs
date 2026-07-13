using Microsoft.Extensions.Logging;

namespace BlokeBot.Twitch.Runtime;

internal sealed class TwitchIrcRuntime(
    ITwitchIrcConnectionSession session,
    TwitchIrcSessionResiliencePipeline resilience,
    ILogger<TwitchRuntimeSessionHealthReport> log,
    TwitchBotRuntimeStatusStore status,
    ITwitchRuntimeIdleWait idleWait
)
{
    public Task RunAsync(CancellationToken stoppingToken)
    {
        return TwitchRuntimeSessionRunner.RunUntilStoppedAsync(
            TwitchBotRuntime.Irc,
            new TwitchRuntimeConnectionTarget.Initial(),
            EstablishSessionAsync,
            TwitchIrcSessionFailureClassifier.Classify,
            log,
            status,
            idleWait,
            stoppingToken
        );
    }

    internal Task<TwitchRuntimeSessionOutcome> EstablishSessionAsync(
        TwitchRuntimeConnectionTarget target,
        CancellationToken stoppingToken
    )
    {
        return TwitchRuntimeSessionRunner.EstablishOnceAsync(
            TwitchBotRuntime.Irc,
            cancellationToken => session.EstablishAsync(target, cancellationToken),
            resilience.ExecuteAsync,
            TwitchIrcSessionFailureClassifier.Classify,
            log,
            status,
            stoppingToken
        );
    }
}
