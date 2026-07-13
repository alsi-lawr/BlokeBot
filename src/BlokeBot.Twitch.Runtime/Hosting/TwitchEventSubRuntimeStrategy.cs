namespace BlokeBot.Twitch.Runtime;

internal sealed class TwitchEventSubRuntimeStrategy(TwitchEventSubRuntime runtime)
    : ITwitchBotRuntimeStrategy
{
    public TwitchBotRuntime Runtime => TwitchBotRuntime.EventSub;

    public Task RunAsync(CancellationToken cancellationToken)
    {
        return runtime.RunAsync(cancellationToken);
    }
}
