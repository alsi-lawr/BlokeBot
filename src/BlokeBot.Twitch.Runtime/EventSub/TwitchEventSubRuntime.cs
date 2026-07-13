using Microsoft.Extensions.Logging;

namespace BlokeBot.Twitch.Runtime;

internal sealed class TwitchEventSubRuntime(
    ITwitchEventSubConnectionSession session,
    TwitchEventSubSessionResiliencePipeline resilience,
    ILogger<TwitchRuntimeSessionHealthReport> log,
    TwitchBotRuntimeStatusStore status,
    ITwitchRuntimeIdleWait idleWait
)
{
    public Task RunAsync(CancellationToken stoppingToken)
    {
        return TwitchRuntimeSessionRunner.RunUntilStoppedAsync(
            TwitchBotRuntime.EventSub,
            new TwitchRuntimeConnectionTarget.Initial(),
            EstablishSessionAsync,
            TwitchEventSubSessionFailureClassifier.Classify,
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
            TwitchBotRuntime.EventSub,
            cancellationToken => session.EstablishAsync(target, cancellationToken),
            resilience.ExecuteAsync,
            TwitchEventSubSessionFailureClassifier.Classify,
            log,
            status,
            stoppingToken
        );
    }
}
