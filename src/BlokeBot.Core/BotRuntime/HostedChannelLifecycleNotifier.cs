using BlokeBot.Core.Features.HostedChannels.Runtime;

namespace BlokeBot.Core.BotRuntime;

internal sealed class HostedChannelLifecycleNotifier(HostedChannelRuntimeLifecycleService lifecycle)
    : IBotChannelLifecycleNotifier
{
    public async Task ChannelStartedAsync(string channel, CancellationToken cancellationToken)
    {
        await lifecycle.MarkStartedAsync(channel, cancellationToken);
    }

    public async Task ChannelStoppedAsync(string channel, CancellationToken cancellationToken)
    {
        await lifecycle.MarkStoppedAsync(channel, cancellationToken);
    }
}
