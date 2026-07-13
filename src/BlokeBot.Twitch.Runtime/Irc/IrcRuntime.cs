namespace BlokeBot.Twitch.Runtime;

internal sealed class IrcRuntime(
    IIrcConnectionSession session,
    IrcSessionResiliencePipeline resilience,
    IRuntimeSessionHealthReporter health,
    BotRuntimeStatusStore status,
    IRuntimeIdleWait idleWait
)
{
    public Task RunAsync(CancellationToken stoppingToken)
    {
        return RuntimeSessionRunner.RunUntilStoppedAsync(
            ChatRuntime.Irc,
            new RuntimeConnectionTarget.Initial(),
            EstablishSessionAsync,
            IrcSessionFailureClassifier.Classify,
            health,
            status,
            idleWait,
            stoppingToken
        );
    }

    internal Task<RuntimeSessionOutcome> EstablishSessionAsync(
        RuntimeConnectionTarget target,
        CancellationToken stoppingToken
    )
    {
        return RuntimeSessionRunner.EstablishOnceAsync(
            ChatRuntime.Irc,
            cancellationToken => session.EstablishAsync(target, cancellationToken),
            resilience.ExecuteAsync,
            IrcSessionFailureClassifier.Classify,
            health,
            status,
            stoppingToken
        );
    }
}
