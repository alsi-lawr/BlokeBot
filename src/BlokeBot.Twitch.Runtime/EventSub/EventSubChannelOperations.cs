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
    IBroadcasterAccountProvider? broadcasters = null
) : IEventSubChannelOperations
{
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
        operationKind is EventSubOperationSubscriptionKind.Raids
            ? CreateConfiguredBotRaidSubscriptionAsync(
                channel,
                authorization,
                account,
                cancellationToken
            )
            : authorization.Match(
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
                    broadcaster.Operation switch
                    {
                        EventSubBroadcasterOperationKind.Polls => CreatePollSubscriptionsAsync(
                            channel,
                            authorization,
                            account,
                            cancellationToken
                        ),
                        EventSubBroadcasterOperationKind.RewardRedemptions =>
                            CreateRewardRedemptionSubscriptionsAsync(
                                channel,
                                authorization,
                                account,
                                cancellationToken
                            ),
                        EventSubBroadcasterOperationKind.Predictions =>
                            CreatePredictionSubscriptionsAsync(
                                channel,
                                authorization,
                                account,
                                cancellationToken
                            ),
                        _ => throw new UnreachableException(
                            "Unknown broadcaster EventSub operation kind."
                        ),
                    }
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

    public ValueTask<bool> NativeTwitchFeatureIsEnabledAsync(
        string channel,
        EventSubOperationSubscriptionKind kind,
        CancellationToken cancellationToken
    )
    {
        var feature = kind switch
        {
            EventSubOperationSubscriptionKind.Shoutouts
            or EventSubOperationSubscriptionKind.Raids => NativeTwitchFeature.Shoutouts,
            EventSubOperationSubscriptionKind.Polls => NativeTwitchFeature.Polls,
            EventSubOperationSubscriptionKind.RewardRedemptions =>
                NativeTwitchFeature.RewardsAndRedemptions,
            EventSubOperationSubscriptionKind.Predictions => NativeTwitchFeature.Predictions,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
        return nativeTwitch.IsEnabledAsync(channel, feature, cancellationToken);
    }

    private async ValueTask<EventSubSubscriptionSetupOutcome> CreateConfiguredBotSubscriptionsAsync(
        string channel,
        EventSubAuthorizationContext authorization,
        BotAccount account,
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
            resolved => CreateChatSubscriptionsAsync(channel, authorization, account, resolved, ct),
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

    private async ValueTask<EventSubSubscriptionSetupOutcome> CreateChatSubscriptionsAsync(
        string channel,
        EventSubAuthorizationContext authorization,
        BotAccount account,
        ChatIdentityResolution.Resolved resolved,
        CancellationToken ct
    )
    {
        var ids = new List<string>();
        try
        {
            ids.Add(
                await CreateAsync(
                    "channel.chat.message",
                    new Dictionary<string, string>
                    {
                        ["broadcaster_user_id"] = resolved.BroadcasterId,
                        ["user_id"] = resolved.BotUserId,
                    },
                    ct
                )
            );
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

    private async ValueTask<EventSubSubscriptionSetupOutcome> CreateConfiguredBotOperationSubscriptionsAsync(
        string channel,
        EventSubAuthorizationContext authorization,
        BotAccount account,
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
                CreateShoutoutSubscriptionsAsync(channel, authorization, account, resolved, ct),
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

    private async ValueTask<EventSubSubscriptionSetupOutcome> CreateShoutoutSubscriptionsAsync(
        string channel,
        EventSubAuthorizationContext authorization,
        BotAccount account,
        ChatIdentityResolution.Resolved resolved,
        CancellationToken ct
    )
    {
        var ids = new List<string>();
        try
        {
            ids.Add(
                await CreateAsync(
                    "channel.shoutout.create",
                    new Dictionary<string, string>
                    {
                        ["broadcaster_user_id"] = resolved.BroadcasterId,
                        ["moderator_user_id"] = resolved.BotUserId,
                    },
                    ct
                )
            );
            ids.Add(
                await CreateAsync(
                    "channel.shoutout.receive",
                    new Dictionary<string, string>
                    {
                        ["broadcaster_user_id"] = resolved.BroadcasterId,
                        ["moderator_user_id"] = resolved.BotUserId,
                    },
                    ct
                )
            );
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

    private ValueTask<EventSubSubscriptionSetupOutcome> CreatePollSubscriptionsAsync(
        string channel,
        EventSubAuthorizationContext authorization,
        BotAccount account,
        CancellationToken ct
    ) =>
        CreateBroadcasterOperationSubscriptionsAsync(
            channel,
            authorization,
            account,
            ["channel.poll.begin", "channel.poll.progress", "channel.poll.end"],
            ct
        );

    private ValueTask<EventSubSubscriptionSetupOutcome> CreateRewardRedemptionSubscriptionsAsync(
        string channel,
        EventSubAuthorizationContext authorization,
        BotAccount account,
        CancellationToken ct
    ) =>
        CreateBroadcasterOperationSubscriptionsAsync(
            channel,
            authorization,
            account,
            [
                "channel.channel_points_custom_reward_redemption.add",
                "channel.channel_points_custom_reward_redemption.update",
            ],
            ct
        );

    private ValueTask<EventSubSubscriptionSetupOutcome> CreatePredictionSubscriptionsAsync(
        string channel,
        EventSubAuthorizationContext authorization,
        BotAccount account,
        CancellationToken ct
    ) =>
        CreateBroadcasterOperationSubscriptionsAsync(
            channel,
            authorization,
            account,
            [
                "channel.prediction.begin",
                "channel.prediction.progress",
                "channel.prediction.lock",
                "channel.prediction.end",
            ],
            ct
        );

    private async ValueTask<EventSubSubscriptionSetupOutcome> CreateBroadcasterOperationSubscriptionsAsync(
        string channel,
        EventSubAuthorizationContext authorization,
        BotAccount account,
        IReadOnlyList<string> subscriptionTypes,
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

        var ids = new List<string>();
        try
        {
            foreach (var type in subscriptionTypes)
            {
                ids.Add(
                    await CreateAsync(
                        type,
                        new Dictionary<string, string> { ["broadcaster_user_id"] = broadcasterId },
                        ct
                    )
                );
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
    ) =>
        subscriptions.CreateAsync(
            settings.Identity.ClientId,
            new EventSubSubscriptionRequest(type, "1", condition),
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
