namespace BlokeBot.Twitch.Runtime;

internal sealed class NoOpBotChannelLifecycleNotifier : IBotChannelLifecycleNotifier
{
    public Task ChannelStartedAsync(string channel, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task ChannelStoppedAsync(string channel, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
