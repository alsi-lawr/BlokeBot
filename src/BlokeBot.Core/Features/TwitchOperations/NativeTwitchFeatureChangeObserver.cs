using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Core.Features.TwitchOperations.ChannelPoints;
using BlokeBot.Core.Features.TwitchOperations.ClipsMarkers;
using BlokeBot.Core.Features.TwitchOperations.Polls;
using BlokeBot.Core.Features.TwitchOperations.Predictions;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.TwitchOperations;

internal sealed class NativeTwitchFeatureChangeObserver(
    IEventSubChannelReconciliationTrigger eventSub,
    PollService polls,
    ClipMarkerService clipsMarkers,
    ChannelPointsService channelPoints,
    PredictionService predictions
) : IHostFeatureActivationObserver
{
    public async ValueTask<HostFeatureAutomaticWorkResult> ApplyAsync(
        HostFeatureActivationChange change,
        CancellationToken cancellationToken
    )
    {
        if (!HostFeatureFlags.NativeTwitchFeatures.Contains(change.Feature))
        {
            return new HostFeatureAutomaticWorkResult.Complete();
        }
        if (change.Feature is not HostFeatureFlags.ClipsAndMarkers)
        {
            await eventSub.ReconcileAsync(cancellationToken);
        }

        if (change.State is HostFeatureActivationState.Disabled)
        {
            return new HostFeatureAutomaticWorkResult.Complete();
        }

        switch (change.Feature)
        {
            case HostFeatureFlags.Polls:
                await polls.ReconcileAsync(change.HostId, cancellationToken);
                break;
            case HostFeatureFlags.ClipsAndMarkers:
                await clipsMarkers.ReconcileAsync(change.HostId, cancellationToken);
                break;
            case HostFeatureFlags.RewardsAndRedemptions:
                await channelPoints.ReconcileAsync(change.HostId, cancellationToken);
                break;
            case HostFeatureFlags.Predictions:
                await predictions.ReconcileAsync(change.HostId, cancellationToken);
                break;
        }

        return new HostFeatureAutomaticWorkResult.Complete();
    }
}
