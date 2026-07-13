namespace BlokeBot.Twitch.Runtime;

internal sealed class EventSubRuntime(
    IEventSubConnectionSession session,
    EventSubSessionResiliencePipeline resilience,
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
            EventSubSessionFailureClassifier.Classify,
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
            EventSubSessionFailureClassifier.Classify,
            health,
            status,
            stoppingToken
        );
    }
}
