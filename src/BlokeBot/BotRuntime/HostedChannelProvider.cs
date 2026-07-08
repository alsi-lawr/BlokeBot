using Alsi.TwitchBot;
using BlokeBot.Features.HostedChannels.Runtime;

namespace BlokeBot.BotRuntime;

internal sealed class HostedChannelProvider(HostedChannelRuntimeStatusService hostedChannels)
    : ITwitchBotChannelProvider
{
    public async ValueTask<IReadOnlyList<string>> GetChannelsAsync(
        CancellationToken cancellationToken
    ) => await hostedChannels.LoadConnectableChannelLoginsAsync(cancellationToken);
}
