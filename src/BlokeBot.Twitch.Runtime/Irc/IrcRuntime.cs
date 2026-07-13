namespace BlokeBot.Twitch.Runtime;

internal sealed class IrcRuntime(
    IIrcConnectionSession session,
    IrcSessionResiliencePipeline resilience,
    ITwitchRuntimeSessionHealthReporter health,
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
            IrcSessionFailureClassifier.Classify,
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
            TwitchBotRuntime.Irc,
            cancellationToken => session.EstablishAsync(target, cancellationToken),
            resilience.ExecuteAsync,
            IrcSessionFailureClassifier.Classify,
            health,
            status,
            stoppingToken
        );
    }
}
