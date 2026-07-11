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
            TwitchBotRuntime.Irc,
            new TwitchRuntimeConnectionTarget.Initial(),
            EstablishSessionAsync,
            TwitchIrcSessionFailureClassifier.Classify,
            health,
            status,
            idleWait,
            stoppingToken
        );

    internal Task<TwitchRuntimeSessionOutcome> EstablishSessionAsync(
        TwitchRuntimeConnectionTarget target,
        CancellationToken stoppingToken
    ) =>
        TwitchRuntimeSessionRunner.EstablishOnceAsync(
            TwitchBotRuntime.Irc,
            cancellationToken => session.EstablishAsync(target, cancellationToken),
            resilience.ExecuteAsync,
            TwitchIrcSessionFailureClassifier.Classify,
            health,
            status,
            stoppingToken
        );
}
