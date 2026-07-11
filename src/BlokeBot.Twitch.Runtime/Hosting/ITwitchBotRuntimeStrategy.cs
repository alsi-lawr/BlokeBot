namespace BlokeBot.Twitch.Runtime;

internal interface ITwitchBotRuntimeStrategy
{
    TwitchBotRuntime Runtime { get; }

    Task RunAsync(CancellationToken cancellationToken);
}
