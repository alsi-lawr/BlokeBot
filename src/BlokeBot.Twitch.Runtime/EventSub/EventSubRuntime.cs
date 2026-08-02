namespace BlokeBot.Twitch.Runtime;

internal sealed class EventSubRuntime(
    IEventSubConnectionSession session,
    EventSubSessionResiliencePipeline resilience,
    IRuntimeSessionHealthReporter health,
    BotRuntimeStatusStore status,
    IRuntimeIdleWait idleWait
)
{
    public Task RunAsync(CancellationToken stoppingToken) =>
        RuntimeSessionRunner.RunUntilStoppedAsync(
            ChatRuntime.EventSub,
            new RuntimeConnectionTarget.Initial(),
            EstablishSessionAsync,
            EventSubSessionFailureClassifier.Classify,
            health,
            status,
            idleWait,
            stoppingToken
        );

    internal Task<RuntimeSessionOutcome> EstablishSessionAsync(
        RuntimeConnectionTarget target,
        CancellationToken stoppingToken
    ) =>
        RuntimeSessionRunner.EstablishOnceAsync(
            ChatRuntime.EventSub,
            cancellationToken => session.EstablishAsync(target, cancellationToken),
            resilience.ExecuteAsync,
            EventSubSessionFailureClassifier.Classify,
            health,
            status,
            stoppingToken
        );
}
