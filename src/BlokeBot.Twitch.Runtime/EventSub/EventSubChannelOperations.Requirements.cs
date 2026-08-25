namespace BlokeBot.Twitch.Runtime;

internal sealed partial class EventSubChannelOperations
{
    public async ValueTask<IReadOnlyList<EventSubExactSubscription>> GetExactRequirementsAsync(
        string channel,
        CancellationToken cancellationToken
    )
    {
        var requirements = new HashSet<EventSubExactSubscription>();
        foreach (var source in _exactRequirements)
        {
            requirements.UnionWith(await source.GetRequirementsAsync(channel, cancellationToken));
        }
        return requirements
            .OrderBy(static requirement => requirement.Type, StringComparer.Ordinal)
            .ThenBy(static requirement => requirement.Version, StringComparer.Ordinal)
            .ToArray();
    }

    public async ValueTask<bool> NativeTwitchFeatureIsEnabledAsync(
        string channel,
        EventSubOperationSubscriptionKind kind,
        CancellationToken cancellationToken
    ) =>
        kind switch
        {
            EventSubOperationSubscriptionKind.Shoutouts => await nativeTwitch.IsEnabledAsync(
                channel,
                NativeTwitchFeature.RaidCollaboration,
                cancellationToken
            )
                || await AutomationRequiresAsync(
                    channel,
                    AutomationEventSubRequirement.Shoutouts,
                    cancellationToken
                ),
            // One incoming channel.raid subscription serves collaboration and automations.
            EventSubOperationSubscriptionKind.Raids => await nativeTwitch.IsEnabledAsync(
                channel,
                NativeTwitchFeature.RaidCollaboration,
                cancellationToken
            )
                || await AutomationRequiresAsync(
                    channel,
                    AutomationEventSubRequirement.IncomingRaids,
                    cancellationToken
                ),
            EventSubOperationSubscriptionKind.OutgoingRaids => await nativeTwitch.IsEnabledAsync(
                channel,
                NativeTwitchFeature.RaidCollaboration,
                cancellationToken
            ),
            EventSubOperationSubscriptionKind.Polls => await nativeTwitch.IsEnabledAsync(
                channel,
                NativeTwitchFeature.Polls,
                cancellationToken
            )
                || await AutomationRequiresAsync(
                    channel,
                    AutomationEventSubRequirement.Polls,
                    cancellationToken
                ),
            EventSubOperationSubscriptionKind.RewardRedemptions =>
                await nativeTwitch.IsEnabledAsync(
                    channel,
                    NativeTwitchFeature.RewardsAndRedemptions,
                    cancellationToken
                )
                    || await AutomationRequiresAsync(
                        channel,
                        AutomationEventSubRequirement.Redemptions,
                        cancellationToken
                    ),
            EventSubOperationSubscriptionKind.Predictions => await nativeTwitch.IsEnabledAsync(
                channel,
                NativeTwitchFeature.Predictions,
                cancellationToken
            )
                || await AutomationRequiresAsync(
                    channel,
                    AutomationEventSubRequirement.Predictions,
                    cancellationToken
                ),
            EventSubOperationSubscriptionKind.AutomationStream => await AutomationRequiresAsync(
                channel,
                AutomationEventSubRequirement.Stream,
                cancellationToken
            ),
            EventSubOperationSubscriptionKind.AutomationChannelUpdates =>
                await AutomationRequiresAsync(
                    channel,
                    AutomationEventSubRequirement.ChannelUpdates,
                    cancellationToken
                ),
            EventSubOperationSubscriptionKind.AutomationFollows => await AutomationRequiresAsync(
                channel,
                AutomationEventSubRequirement.Follows,
                cancellationToken
            ),
            EventSubOperationSubscriptionKind.AutomationSubscriptions =>
                await AutomationRequiresAsync(
                    channel,
                    AutomationEventSubRequirement.Subscriptions,
                    cancellationToken
                ),
            EventSubOperationSubscriptionKind.AutomationCheers => await AutomationRequiresAsync(
                channel,
                AutomationEventSubRequirement.Cheers,
                cancellationToken
            ),
            EventSubOperationSubscriptionKind.AutomationHypeTrain => await AutomationRequiresAsync(
                channel,
                AutomationEventSubRequirement.HypeTrain,
                cancellationToken
            ),
            EventSubOperationSubscriptionKind.AutomationChatNotifications =>
                await AutomationRequiresAsync(
                    channel,
                    AutomationEventSubRequirement.ChatNotifications,
                    cancellationToken
                ),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };

    private async ValueTask<bool> AutomationRequiresAsync(
        string channel,
        AutomationEventSubRequirement requirement,
        CancellationToken cancellationToken
    )
    {
        if (
            automationRequirements is not null
            && await automationRequirements.RequiresAsync(channel, requirement, cancellationToken)
        )
        {
            return true;
        }

        foreach (var source in _eventRequirements)
        {
            if (await source.RequiresAsync(channel, requirement, cancellationToken))
            {
                return true;
            }
        }
        return false;
    }
}
