using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.TwitchOperations.ChannelPoints;
using BlokeBot.Core.Features.TwitchOperations.ClipsMarkers;
using BlokeBot.Core.Features.TwitchOperations.Polls;
using BlokeBot.Core.Features.TwitchOperations.Predictions;
using BlokeBot.Persistence.Models;
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
        HostFeatureFlags feature,
        NativeTwitchFeatureState state,
        CancellationToken cancellationToken
    )
    {
        if (feature is not HostFeatureFlags.ClipsAndMarkers)
        {
            await eventSub.ReconcileAsync(cancellationToken);
        }

        if (state is NativeTwitchFeatureState.Disabled)
        {
            return;
        }

        switch (feature)
        {
            case HostFeatureFlags.Polls:
                await polls.ReconcileAsync(hostId, cancellationToken);
                break;
            case HostFeatureFlags.ClipsAndMarkers:
                await clipsMarkers.ReconcileAsync(hostId, cancellationToken);
                break;
            case HostFeatureFlags.RewardsAndRedemptions:
                await channelPoints.ReconcileAsync(hostId, cancellationToken);
                break;
            case HostFeatureFlags.Predictions:
                await predictions.ReconcileAsync(hostId, cancellationToken);
                break;
        }
    }
}
