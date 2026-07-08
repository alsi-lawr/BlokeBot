using BlokeBot.Features.HostedChannels.Runtime;

namespace BlokeBot.BotRuntime;

internal sealed class HostedChannelLifecycleNotifier(HostedChannelRuntimeLifecycleService lifecycle)
    : ITwitchBotChannelLifecycleNotifier
{
    public async Task ChannelStartedAsync(string channel, CancellationToken cancellationToken) =>
        await lifecycle.MarkStartedAsync(channel, cancellationToken);

    public async Task ChannelStoppedAsync(string channel, CancellationToken cancellationToken) =>
        await lifecycle.MarkStoppedAsync(channel, cancellationToken);
}
