using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Features.TwitchOperations.ChannelPoints;
using BlokeBot.Core.Features.TwitchOperations.ClipsMarkers;
using BlokeBot.Core.Features.TwitchOperations.Polls;
using BlokeBot.Core.Features.TwitchOperations.Predictions;

namespace BlokeBot.Core.BotRuntime;

internal sealed class HostedChannelLifecycleNotifier(
    HostedChannelRuntimeLifecycleService lifecycle,
    PollService polls,
    ClipMarkerService clipsMarkers,
    ChannelPointsService? channelPoints = null,
    PredictionService? predictions = null
) : IBotChannelLifecycleNotifier
{
    public async Task ChannelStartedAsync(string channel, CancellationToken cancellationToken)
    {
        await lifecycle.MarkStartedAsync(channel, cancellationToken);
        await polls.ReconcileChannelAsync(channel, cancellationToken);
        await clipsMarkers.ReconcileChannelAsync(channel, cancellationToken);
        if (channelPoints is not null)
        {
            await channelPoints.ReconcileChannelAsync(channel, cancellationToken);
        }
        if (predictions is not null)
        {
            await predictions.ReconcileChannelAsync(channel, cancellationToken);
        }
    }

    public async Task ChannelStoppedAsync(string channel, CancellationToken cancellationToken)
    {
        await lifecycle.MarkStoppedAsync(channel, cancellationToken);
    }
}
