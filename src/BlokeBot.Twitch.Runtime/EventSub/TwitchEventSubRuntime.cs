namespace BlokeBot.Twitch.Runtime;

internal sealed class TwitchEventSubRuntime(
    ITwitchEventSubConnectionSession session,
    TwitchEventSubSessionResiliencePipeline resilience,
    ITwitchRuntimeSessionHealthReporter health,
    TwitchBotRuntimeStatusStore status,
    ITwitchRuntimeIdleWait idleWait
)
{
    public Task RunAsync(CancellationToken stoppingToken) =>
        TwitchRuntimeSessionRunner.RunUntilStoppedAsync(
            RunSessionAsync,
            idleWait,
            stoppingToken
        );

    internal Task<TwitchRuntimeSessionOutcome> RunSessionAsync(
        CancellationToken stoppingToken
    ) =>
        TwitchRuntimeSessionRunner.RunOnceAsync(
            TwitchBotRuntime.EventSub,
            session.RunAsync,
            resilience.ExecuteAsync,
            TwitchEventSubSessionFailureClassifier.Classify,
            health,
            status,
            stoppingToken
        );
}
