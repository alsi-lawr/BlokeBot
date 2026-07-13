namespace BlokeBot.Twitch.Runtime;

internal sealed class TwitchEventSubRuntime(
    ITwitchEventSubConnectionSession session,
    TwitchEventSubSessionResiliencePipeline resilience,
    ITwitchRuntimeSessionHealthReporter health,
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
            health,
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
            health,
            status,
            stoppingToken
        );
    }
}
