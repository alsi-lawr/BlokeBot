using BlokeBot.Core.Features.HostedChannels.Runtime;

namespace BlokeBot.Core.BotRuntime;

internal sealed class HostedChannelProvider(HostedChannelRuntimeStatusService hostedChannels)
    : IBotChannelProvider
{
    public async ValueTask<IReadOnlyList<string>> GetChannelsAsync(
        CancellationToken cancellationToken
    )
    {
        return await hostedChannels.LoadConnectableChannelLoginsAsync(cancellationToken);
    }
}
