using BlokeBot.Core.Features.HostedChannels.Runtime;

namespace BlokeBot.Core.BotRuntime;

internal sealed class HostedChannelProvider(HostedChannelRuntimeStatusService hostedChannels)
    : IBotChannelProvider
{
    public async ValueTask<IReadOnlyList<BotChannelTarget>> GetChannelsAsync(
        CancellationToken cancellationToken
    ) => await hostedChannels.LoadConnectableChannelTargetsAsync(cancellationToken);
}
