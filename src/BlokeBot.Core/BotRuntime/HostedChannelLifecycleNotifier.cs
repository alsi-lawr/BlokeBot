using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Features.TwitchOperations.ClipsMarkers;
using BlokeBot.Core.Features.TwitchOperations.Polls;

namespace BlokeBot.Core.BotRuntime;

internal sealed class HostedChannelLifecycleNotifier(
    HostedChannelRuntimeLifecycleService lifecycle,
    PollService polls,
    ClipMarkerService clipsMarkers
) : IBotChannelLifecycleNotifier
{
    public async Task ChannelStartedAsync(string channel, CancellationToken cancellationToken)
    {
        await lifecycle.MarkStartedAsync(channel, cancellationToken);
        await polls.ReconcileChannelAsync(channel, cancellationToken);
        await clipsMarkers.ReconcileChannelAsync(channel, cancellationToken);
    }

    public async Task ChannelStoppedAsync(string channel, CancellationToken cancellationToken)
    {
        await lifecycle.MarkStoppedAsync(channel, cancellationToken);
    }
}
