namespace BlokeBot.Twitch.Runtime;

internal sealed class TwitchIrcRuntimeStrategy(TwitchIrcRuntime runtime)
    : ITwitchBotRuntimeStrategy
{
    public TwitchBotRuntime Runtime => TwitchBotRuntime.Irc;

    public Task RunAsync(CancellationToken cancellationToken) =>
        runtime.RunAsync(cancellationToken);
}
