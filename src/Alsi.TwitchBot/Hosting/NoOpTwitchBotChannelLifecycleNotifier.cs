namespace Alsi.TwitchBot;

internal sealed class NoOpTwitchBotChannelLifecycleNotifier : ITwitchBotChannelLifecycleNotifier
{
    public Task ChannelStartedAsync(string channel, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task ChannelStoppedAsync(string channel, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
