namespace BlokeBot.Twitch.Runtime;

internal sealed class NoOpBotChannelLifecycleNotifier : IBotChannelLifecycleNotifier
{
    public Task ChannelStartedAsync(BotChannelTarget target, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task ChannelStoppedAsync(BotChannelTarget target, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
