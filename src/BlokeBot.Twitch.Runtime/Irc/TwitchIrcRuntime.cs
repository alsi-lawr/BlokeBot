namespace BlokeBot.Twitch.Runtime;

internal sealed class TwitchIrcRuntime(
    ITwitchIrcConnectionSession session,
    TwitchIrcSessionResiliencePipeline resilience,
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
            TwitchBotRuntime.Irc,
            session.RunAsync,
            resilience.ExecuteAsync,
            TwitchIrcSessionFailureClassifier.Classify,
            health,
            status,
            stoppingToken
        );
}
