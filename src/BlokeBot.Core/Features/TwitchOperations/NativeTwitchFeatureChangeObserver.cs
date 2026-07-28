using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.TwitchOperations.ChannelPoints;
using BlokeBot.Core.Features.TwitchOperations.ClipsMarkers;
using BlokeBot.Core.Features.TwitchOperations.Polls;
using BlokeBot.Core.Features.TwitchOperations.Predictions;
using BlokeBot.Twitch.Runtime;

namespace BlokeBot.Core.Features.TwitchOperations;

internal sealed class NativeTwitchFeatureChangeObserver(
    IEventSubChannelReconciliationTrigger eventSub,
    PollService polls,
    ClipMarkerService clipsMarkers,
    ChannelPointsService channelPoints,
    PredictionService predictions
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
        await channelPoints.ReconcileAsync(hostId, cancellationToken);
        await predictions.ReconcileAsync(hostId, cancellationToken);
    }
}
