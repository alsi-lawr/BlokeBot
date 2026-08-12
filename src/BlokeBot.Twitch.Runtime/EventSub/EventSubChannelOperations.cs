using System.Diagnostics;
using BlokeBot.Functional;

namespace BlokeBot.Twitch.Runtime;

internal sealed class EventSubChannelOperations(
    BotSettings settings,
    IBotAccountProvider accounts,
    ChatIdentityResolver identities,
    IEventSubSubscriptionTransport subscriptions,
    IStartupChatMessageProvider startupMessages,
    IPublicChatMessageSender sender,
    IBotChannelLifecycleNotifier lifecycle,
    INativeTwitchFeatureStateProvider nativeTwitch,
    IBroadcasterAccountProvider? broadcasters = null,
    IAutomationEventSubRequirementSource? automationRequirements = null,
    IEnumerable<IEventSubRequirementSource>? eventRequirements = null
) : IEventSubChannelOperations
{
    private readonly IEventSubRequirementSource[] _eventRequirements = [.. eventRequirements ?? []];
    private static readonly IReadOnlyDictionary<
        EventSubBroadcasterOperationKind,
        IReadOnlyList<(string Type, string Version)>
    > _broadcasterOperationSubscriptions = new Dictionary<
        EventSubBroadcasterOperationKind,
        IReadOnlyList<(string Type, string Version)>
    >
    {
        [EventSubBroadcasterOperationKind.Polls] =
        [
            ("channel.poll.begin", "1"),
            ("channel.poll.progress", "1"),
            ("channel.poll.end", "1"),
        ],
        [EventSubBroadcasterOperationKind.RewardRedemptions] =
        [
            ("channel.channel_points_custom_reward_redemption.add", "1"),
            ("channel.channel_points_custom_reward_redemption.update", "1"),
        ],
        [EventSubBroadcasterOperationKind.Predictions] =
        [
            ("channel.prediction.begin", "1"),
            ("channel.prediction.progress", "1"),
            ("channel.prediction.lock", "1"),
            ("channel.prediction.end", "1"),
        ],
        [EventSubBroadcasterOperationKind.AutomationSubscriptions] =
        [
            ("channel.subscribe", "1"),
            ("channel.subscription.gift", "1"),
        ],
        [EventSubBroadcasterOperationKind.AutomationCheers] = [("channel.cheer", "1")],
        [EventSubBroadcasterOperationKind.AutomationHypeTrain] =
        [
            ("channel.hype_train.begin", "2"),
            ("channel.hype_train.progress", "2"),
            ("channel.hype_train.end", "2"),
        ],
    };

    public IO<BotAccount, AccessTokenUnavailableReason> ResolveAccount(
        string channel,
        EventSubAuthorizationContext authorization
    ) =>
        authorization.Match(
            _ => accounts.GetBotAccount(channel),
            _ => accounts.GetBotAccount(channel),
            _ =>
                broadcasters?.GetBroadcasterAccount(channel)
                ?? IO<BotAccount, AccessTokenUnavailableReason>.Create(_ =>
                    ValueTask.FromResult(
                        Result<BotAccount, AccessTokenUnavailableReason>.Error(
                            AccessTokenUnavailableReason.BroadcasterAuthorizationUnavailable
                        )
                    )
                )
        );

    public ValueTask<EventSubSubscriptionSetupOutcome> CreateSubscriptionAsync(
        string channel,
        EventSubAuthorizationContext authorization,
        BotAccount account,
        CancellationToken cancellationToken,
        EventSubOperationSubscriptionKind? operationKind = null
    ) =>
        operationKind switch
        {
            EventSubOperationSubscriptionKind.Raids => CreateConfiguredBotRaidSubscriptionAsync(
                channel,
                authorization,
                account,
                cancellationToken
            ),
            EventSubOperationSubscriptionKind.OutgoingRaids =>
                CreateConfiguredBotOutgoingRaidSubscriptionAsync(
                    channel,
                    authorization,
                    account,
                    cancellationToken
                ),
            EventSubOperationSubscriptionKind.AutomationStream =>
                CreateAutomationStreamSubscriptionsAsync(
                    channel,
                    authorization,
                    account,
                    cancellationToken
                ),
            EventSubOperationSubscriptionKind.AutomationChannelUpdates =>
                CreateBroadcasterOperationSubscriptionsAsync(
                    channel,
                    authorization,
                    account,
                    [("channel.update", "2")],
                    cancellationToken
                ),
            EventSubOperationSubscriptionKind.AutomationFollows =>
                CreateAutomationBotConditionSubscriptionsAsync(
                    channel,
                    authorization,
                    account,
                    [("channel.follow", "2", "moderator_user_id")],
                    cancellationToken
                ),
            EventSubOperationSubscriptionKind.AutomationChatNotifications =>
                CreateAutomationBotConditionSubscriptionsAsync(
                    channel,
                    authorization,
                    account,
                    [("channel.chat.notification", "1", "user_id")],
                    cancellationToken
                ),
            _ => CreateAuthorizedSubscriptionAsync(
                channel,
                authorization,
                account,
                cancellationToken
            ),
        };

    private ValueTask<EventSubSubscriptionSetupOutcome> CreateAuthorizedSubscriptionAsync(
        string channel,
        EventSubAuthorizationContext authorization,
        BotAccount account,
        CancellationToken cancellationToken
    ) =>
        authorization.Match(
            _ =>
                CreateConfiguredBotSubscriptionsAsync(
                    channel,
                    authorization,
                    account,
                    cancellationToken
                ),
            _ =>
                CreateConfiguredBotOperationSubscriptionsAsync(
                    channel,
                    authorization,
                    account,
                    cancellationToken
                ),
            broadcaster =>
                _broadcasterOperationSubscriptions.TryGetValue(
                    broadcaster.Operation,
                    out var subscriptionTypes
                )
                    ? CreateBroadcasterOperationSubscriptionsAsync(
                        channel,
                        authorization,
                        account,
                        subscriptionTypes,
                        cancellationToken
                    )
                    : throw new UnreachableException("Unknown broadcaster EventSub operation kind.")
        );

    private async ValueTask<EventSubSubscriptionSetupOutcome> CreateConfiguredBotRaidSubscriptionAsync(
        string channel,
        EventSubAuthorizationContext authorization,
        BotAccount account,
        CancellationToken ct
    )
    {
        var broadcasterId = await identities.ResolveBroadcasterIdAsync(
            channel,
            account.AccessToken,
            ct
        );
        if (string.IsNullOrWhiteSpace(broadcasterId))
        {
            return new EventSubSubscriptionSetupOutcome.MissingChannel();
        }

        var id = await CreateAsync(
            "channel.raid",
            new Dictionary<string, string> { ["to_broadcaster_user_id"] = broadcasterId },
            ct
        );
        return new EventSubSubscriptionSetupOutcome.Created(
            CreateActive(channel, authorization, account, [id])
        );
    }

    private async ValueTask<EventSubSubscriptionSetupOutcome> CreateConfiguredBotOutgoingRaidSubscriptionAsync(
        string channel,
        EventSubAuthorizationContext authorization,
        BotAccount account,
        CancellationToken ct
    )
    {
        var broadcasterId = await identities.ResolveBroadcasterIdAsync(
            channel,
            account.AccessToken,
            ct
        );
        if (string.IsNullOrWhiteSpace(broadcasterId))
        {
            return new EventSubSubscriptionSetupOutcome.MissingChannel();
        }

        var id = await CreateAsync(
            "channel.raid",
            new Dictionary<string, string> { ["from_broadcaster_user_id"] = broadcasterId },
            ct
        );
        return new EventSubSubscriptionSetupOutcome.Created(
            CreateActive(channel, authorization, account, [id])
        );
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
                NativeTwitchFeature.Shoutouts,
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

    private ValueTask<EventSubSubscriptionSetupOutcome> CreateConfiguredBotSubscriptionsAsync(
        string channel,
        EventSubAuthorizationContext authorization,
        BotAccount account,
        CancellationToken ct
    ) =>
        CreateBotConditionSubscriptionsAsync(
            channel,
            authorization,
            account,
            [("channel.chat.message", "1", "user_id")],
            ct
        );

    private ValueTask<EventSubSubscriptionSetupOutcome> CreateConfiguredBotOperationSubscriptionsAsync(
        string channel,
        EventSubAuthorizationContext authorization,
        BotAccount account,
        CancellationToken ct
    ) =>
        CreateBotConditionSubscriptionsAsync(
            channel,
            authorization,
            account,
            [
                ("channel.shoutout.create", "1", "moderator_user_id"),
                ("channel.shoutout.receive", "1", "moderator_user_id"),
            ],
            ct
        );

    private async ValueTask<EventSubSubscriptionSetupOutcome> CreateAutomationStreamSubscriptionsAsync(
        string channel,
        EventSubAuthorizationContext authorization,
        BotAccount account,
        CancellationToken ct
    ) =>
        await CreateBroadcasterOperationSubscriptionsAsync(
            channel,
            authorization,
            account,
            [("stream.online", "1"), ("stream.offline", "1")],
            ct
        );

    private ValueTask<EventSubSubscriptionSetupOutcome> CreateAutomationBotConditionSubscriptionsAsync(
        string channel,
        EventSubAuthorizationContext authorization,
        BotAccount account,
        IReadOnlyList<(string Type, string Version, string BotConditionKey)> subscriptionTypes,
        CancellationToken ct
    ) =>
        CreateBotConditionSubscriptionsAsync(
            channel,
            authorization,
            account,
            subscriptionTypes,
            ct
        );

    private async ValueTask<EventSubSubscriptionSetupOutcome> CreateBotConditionSubscriptionsAsync(
        string channel,
        EventSubAuthorizationContext authorization,
        BotAccount account,
        IReadOnlyList<(string Type, string Version, string BotConditionKey)> subscriptionTypes,
        CancellationToken ct
    )
    {
        var resolution = await identities.ResolveAsync(
            channel,
            account.Login,
            account.AccessToken,
            ct
        );
        return await resolution.Match(
            resolved =>
                CreateSubscriptionsAsync(
                    channel,
                    authorization,
                    account,
                    subscriptionTypes,
                    (subscription, token) =>
                        CreateAsync(
                            subscription.Type,
                            subscription.Version,
                            new Dictionary<string, string>
                            {
                                ["broadcaster_user_id"] = resolved.BroadcasterId,
                                [subscription.BotConditionKey] = resolved.BotUserId,
                            },
                            token
                        ),
                    ct
                ),
            static _ =>
                ValueTask.FromResult<EventSubSubscriptionSetupOutcome>(
                    new EventSubSubscriptionSetupOutcome.MissingChannel()
                ),
            static _ =>
                ValueTask.FromResult<EventSubSubscriptionSetupOutcome>(
                    new EventSubSubscriptionSetupOutcome.MissingBot()
                )
        );
    }

    private async ValueTask<EventSubSubscriptionSetupOutcome> CreateBroadcasterOperationSubscriptionsAsync(
        string channel,
        EventSubAuthorizationContext authorization,
        BotAccount account,
        IReadOnlyList<(string Type, string Version)> subscriptionTypes,
        CancellationToken ct
    )
    {
        var broadcasterId = await identities.ResolveBroadcasterIdAsync(
            channel,
            account.AccessToken,
            ct
        );
        return string.IsNullOrWhiteSpace(broadcasterId)
            ? new EventSubSubscriptionSetupOutcome.MissingChannel()
            : await CreateSubscriptionsAsync(
                channel,
                authorization,
                account,
                subscriptionTypes,
                (subscription, token) =>
                    CreateAsync(
                        subscription.Type,
                        subscription.Version,
                        new Dictionary<string, string> { ["broadcaster_user_id"] = broadcasterId },
                        token
                    ),
                ct
            );
    }

    private async ValueTask<EventSubSubscriptionSetupOutcome> CreateSubscriptionsAsync<TSubscription>(
        string channel,
        EventSubAuthorizationContext authorization,
        BotAccount account,
        IReadOnlyList<TSubscription> subscriptionTypes,
        Func<TSubscription, CancellationToken, Task<string>> createAsync,
        CancellationToken ct
    )
    {
        var ids = new List<string>();
        try
        {
            foreach (var subscription in subscriptionTypes)
            {
                ids.Add(await createAsync(subscription, ct));
            }
            return new EventSubSubscriptionSetupOutcome.Created(
                CreateActive(channel, authorization, account, ids)
            );
        }
        catch (Exception exception) when (ids.Count > 0)
        {
            return new EventSubSubscriptionSetupOutcome.PartiallyCreated(
                CreateActive(channel, authorization, account, ids),
                exception
            );
        }
    }

    public async ValueTask<EventSubStartupDeliveryOutcome> DeliverStartupMessageAsync(
        string channel,
        CancellationToken ct
    )
    {
        var startupMessage = await startupMessages.GetAsync(channel, ct);
        if (startupMessage is StartupChatMessage.Disabled)
        {
            return new EventSubStartupDeliveryOutcome.Completed();
        }
        var outcome = await sender.SendAsync(
            channel,
            ((StartupChatMessage.Enabled)startupMessage).Text,
            new PublicChatDeliveryDeadline.ConfiguredMaximum(),
            ct
        );
        return outcome.Match<EventSubStartupDeliveryOutcome>(
            static _ => new EventSubStartupDeliveryOutcome.Completed(),
            static _ => new EventSubStartupDeliveryOutcome.Rejected()
        );
    }

    public ValueTask NotifyChannelStartedAsync(string channel, CancellationToken ct) =>
        new(lifecycle.ChannelStartedAsync(channel, ct));

    public async ValueTask<EventSubSubscriptionDeletionOutcome> DeleteSubscriptionAsync(
        ActiveEventSubSubscription subscription,
        CancellationToken ct
    )
    {
        try
        {
            await subscriptions.DeleteAsync(
                settings.Identity.ClientId,
                subscription.SubscriptionId,
                ct
            );
            foreach (var id in subscription.AdditionalSubscriptionIds)
            {
                await subscriptions.DeleteAsync(settings.Identity.ClientId, id, ct);
            }
            return new EventSubSubscriptionDeletionOutcome.Deleted();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new EventSubSubscriptionDeletionOutcome.Unresolved
            {
                Failure = EventSubChannelFailureClassifier.Classify(
                    exception,
                    EventSubChannelPhase.SubscriptionDeletion,
                    ct
                ),
            };
        }
    }

    public ValueTask CompleteStopAsync(string channel, CancellationToken ct) =>
        new(lifecycle.ChannelStoppedAsync(channel, ct));

    private Task<string> CreateAsync(
        string type,
        IReadOnlyDictionary<string, string> condition,
        CancellationToken cancellationToken
    ) => CreateAsync(type, "1", condition, cancellationToken);

    private Task<string> CreateAsync(
        string type,
        string version,
        IReadOnlyDictionary<string, string> condition,
        CancellationToken cancellationToken
    ) =>
        subscriptions.CreateAsync(
            settings.Identity.ClientId,
            new EventSubSubscriptionRequest(type, version, condition),
            cancellationToken
        );

    private static ActiveEventSubSubscription CreateActive(
        string channel,
        EventSubAuthorizationContext authorization,
        BotAccount account,
        IReadOnlyList<string> ids
    ) =>
        new()
        {
            Channel = channel,
            SubscriptionId = ids[0],
            AdditionalSubscriptionIds = ids.Skip(1).ToArray(),
            BotLogin = account.Login,
            Authorization = authorization,
            Readiness = EventSubSubscriptionReadiness.PendingStartupDelivery,
        };
}
