namespace BlokeBot.Twitch.Runtime;

internal sealed partial class EventSubDeliveryHandler
{
    private async Task DispatchShoutoutAsync(
        EventSubShoutoutEvent shoutout,
        CancellationToken cancellationToken
    )
    {
        await NotifyPluginObserversAsync(
            (observer, token) => observer.ShoutoutOccurredAsync(shoutout, token),
            cancellationToken
        );
        if (
            !await nativeTwitch.IsEnabledAsync(
                shoutout.BroadcasterUserLogin,
                NativeTwitchFeature.RaidCollaboration,
                cancellationToken
            )
        )
        {
            return;
        }

        foreach (var observer in _shoutoutObservers)
        {
            await observer.ShoutoutReceivedAsync(shoutout, cancellationToken);
        }

        await NotifyCoreAutomationObserversAsync(
            (observer, token) => observer.ShoutoutOccurredAsync(shoutout, token),
            cancellationToken
        );
    }

    private async Task NotifyAutomationObserversAsync(
        Func<ITwitchEventAutomationObserver, CancellationToken, Task> notify,
        CancellationToken cancellationToken
    )
    {
        await NotifyCoreAutomationObserversAsync(notify, cancellationToken);
        foreach (var observer in _pluginObservers)
        {
            await notify(observer, cancellationToken);
        }
    }

    private async Task NotifyCoreAutomationObserversAsync(
        Func<ITwitchEventAutomationObserver, CancellationToken, Task> notify,
        CancellationToken cancellationToken
    )
    {
        foreach (var observer in _automationObservers)
        {
            await notify(observer, cancellationToken);
        }
    }

    private async Task NotifyPluginObserversAsync(
        Func<IPluginTwitchEventObserver, CancellationToken, Task> notify,
        CancellationToken cancellationToken
    )
    {
        foreach (var observer in _pluginObservers)
        {
            await notify(observer, cancellationToken);
        }
    }

    private async Task DispatchPollAsync(
        EventSubPollEvent poll,
        CancellationToken cancellationToken
    )
    {
        await NotifyPluginObserversAsync(
            (observer, token) => observer.PollChangedAsync(poll, token),
            cancellationToken
        );
        if (
            !await nativeTwitch.IsEnabledAsync(
                poll.BroadcasterUserLogin,
                NativeTwitchFeature.Polls,
                cancellationToken
            )
        )
        {
            return;
        }

        foreach (var observer in _pollObservers)
        {
            await observer.PollReceivedAsync(poll, cancellationToken);
        }

        await NotifyCoreAutomationObserversAsync(
            (observer, token) => observer.PollChangedAsync(poll, token),
            cancellationToken
        );
    }

    internal async Task DispatchIncomingRaidAsync(
        EventSubIncomingRaidEvent incomingRaid,
        CancellationToken cancellationToken
    )
    {
        // Automation observers gate on their own feature switch, not on Raid & collaboration.
        if (incomingRaid.SubscriptionDirection is EventSubRaidSubscriptionDirection.Incoming)
        {
            await NotifyAutomationObserversAsync(
                (observer, token) => observer.IncomingRaidReceivedAsync(incomingRaid, token),
                cancellationToken
            );
        }
        var targetEnabled =
            incomingRaid.SubscriptionDirection is EventSubRaidSubscriptionDirection.Incoming
            && await nativeTwitch.IsEnabledAsync(
                incomingRaid.ToBroadcasterUserLogin,
                NativeTwitchFeature.RaidCollaboration,
                cancellationToken
            );
        var sourceEnabled =
            incomingRaid.SubscriptionDirection is EventSubRaidSubscriptionDirection.Outgoing
            && await nativeTwitch.IsEnabledAsync(
                incomingRaid.FromBroadcasterUserLogin,
                NativeTwitchFeature.RaidCollaboration,
                cancellationToken
            );
        if (!targetEnabled && !sourceEnabled)
        {
            return;
        }

        foreach (var observer in _incomingRaidObservers)
        {
            await observer.IncomingRaidReceivedAsync(incomingRaid, cancellationToken);
        }
    }

    internal async Task DispatchPredictionAsync(
        EventSubPredictionEvent prediction,
        CancellationToken cancellationToken
    )
    {
        await NotifyPluginObserversAsync(
            (observer, token) => observer.PredictionChangedAsync(prediction, token),
            cancellationToken
        );
        if (
            !await nativeTwitch.IsEnabledAsync(
                prediction.BroadcasterUserLogin,
                NativeTwitchFeature.Predictions,
                cancellationToken
            )
        )
        {
            return;
        }

        foreach (var observer in _predictionObservers)
        {
            await observer.PredictionReceivedAsync(prediction, cancellationToken);
        }

        await NotifyCoreAutomationObserversAsync(
            (observer, token) => observer.PredictionChangedAsync(prediction, token),
            cancellationToken
        );
    }

    internal async Task DispatchRewardRedemptionAsync(
        EventSubRewardRedemptionEvent redemption,
        CancellationToken cancellationToken
    )
    {
        await NotifyPluginObserversAsync(
            (observer, token) => observer.RewardRedemptionReceivedAsync(redemption, token),
            cancellationToken
        );
        if (
            !await nativeTwitch.IsEnabledAsync(
                redemption.BroadcasterUserLogin,
                NativeTwitchFeature.RewardsAndRedemptions,
                cancellationToken
            )
        )
        {
            return;
        }

        foreach (var observer in _channelPointsObservers)
        {
            await observer.RedemptionReceivedAsync(redemption, cancellationToken);
        }

        // The Rewards & redemptions parent gate above also bounds automation dispatch; automation
        // observers run after the Channel Points observers so the redemption row exists before any
        // flow acts on it, and they additionally enforce the Automations switch and the durable
        // delivery receipt themselves.
        await NotifyCoreAutomationObserversAsync(
            (observer, token) => observer.RewardRedemptionReceivedAsync(redemption, token),
            cancellationToken
        );
    }
}
