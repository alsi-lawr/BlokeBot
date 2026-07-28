using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.TwitchOperations.ClipsMarkers;
using BlokeBot.Core.Features.TwitchOperations.Polls;
using BlokeBot.Twitch.Runtime;

namespace BlokeBot.Core.Features.TwitchOperations;

internal sealed class NativeTwitchFeatureChangeObserver(
    IEventSubChannelReconciliationTrigger eventSub,
    PollService polls,
    ClipMarkerService clipsMarkers
) : INativeTwitchFeatureChangeObserver
{
    public async Task NativeTwitchFeatureChangedAsync(
        int hostId,
        NativeTwitchFeatureState state,
        CancellationToken cancellationToken
    )
    {
        await eventSub.ReconcileAsync(cancellationToken);
        if (state is NativeTwitchFeatureState.Disabled)
        {
            return;
        }

        await polls.ReconcileAsync(hostId, cancellationToken);
        await clipsMarkers.ReconcileAsync(hostId, cancellationToken);
    }
}
