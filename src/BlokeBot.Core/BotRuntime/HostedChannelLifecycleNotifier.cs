using BlokeBot.Core.Features.HostedChannels.Runtime;
using BlokeBot.Core.Features.Points.WatchTime;
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
    PredictionService? predictions = null,
    WatchTimeRuntime? watchTime = null
) : IBotChannelLifecycleNotifier
{
    public async Task ChannelStartedAsync(
        BotChannelTarget target,
        CancellationToken cancellationToken
    )
    {
        if (!await lifecycle.MarkStartedAsync(target, cancellationToken))
        {
            return;
        }

        watchTime?.AcceptedStarted(target);
        var channel = target.Channel;
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

    public async Task ChannelStoppedAsync(
        BotChannelTarget target,
        CancellationToken cancellationToken
    )
    {
        if (await lifecycle.MarkStoppedAsync(target, cancellationToken))
        {
            watchTime?.AcceptedStopped(target);
        }
    }
}
