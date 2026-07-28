using BlokeBot.Functional;

namespace BlokeBot.Twitch.Runtime;

internal sealed class EventSubChannelOperations(
    BotSettings settings,
    IBotAccountProvider accounts,
    ChatIdentityResolver identities,
    EventSubClient eventSub,
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
    )
    {
        return authorization.Match(
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
    }

    public ValueTask<EventSubSubscriptionSetupOutcome> CreateSubscriptionAsync(
        string channel,
        EventSubAuthorizationContext authorization,
        BotAccount account,
        string sessionId,
        CancellationToken cancellationToken
    )
    {
        return authorization.Match(
            _ =>
                CreateConfiguredBotSubscriptionsAsync(
                    channel,
                    authorization,
                    account,
                    sessionId,
                    cancellationToken
                ),
            _ =>
                CreateConfiguredBotOperationSubscriptionsAsync(
                    channel,
                    authorization,
                    account,
                    sessionId,
                    cancellationToken
                ),
            _ =>
                CreatePollSubscriptionsAsync(
                    channel,
                    authorization,
                    account,
                    sessionId,
                    cancellationToken
                )
        );
    }

    public ValueTask<bool> NativeTwitchIsEnabledAsync(
        string channel,
        CancellationToken cancellationToken
    )
    {
        return nativeTwitch.IsEnabledAsync(channel, cancellationToken);
    }

    private async ValueTask<EventSubSubscriptionSetupOutcome> CreateConfiguredBotSubscriptionsAsync(
        string channel,
        EventSubAuthorizationContext authorization,
        BotAccount account,
        string sessionId,
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
                CreateChatSubscriptionsAsync(
                    channel,
                    authorization,
                    account,
                    sessionId,
                    resolved,
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

    private async ValueTask<EventSubSubscriptionSetupOutcome> CreateChatSubscriptionsAsync(
        string channel,
        EventSubAuthorizationContext authorization,
        BotAccount account,
        string sessionId,
        ChatIdentityResolution.Resolved resolved,
        CancellationToken ct
    )
    {
        var ids = new List<string>();
        try
        {
            var context = new HelixRequestContext(settings.Identity.ClientId, account.AccessToken);
            ids.Add(
                await eventSub.CreateChatMessageSubscriptionAsync(
                    context,
                    resolved.BroadcasterId,
                    resolved.BotUserId,
                    sessionId,
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
        string sessionId,
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
                CreateShoutoutSubscriptionsAsync(
                    channel,
                    authorization,
                    account,
                    sessionId,
                    resolved,
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

    private async ValueTask<EventSubSubscriptionSetupOutcome> CreateShoutoutSubscriptionsAsync(
        string channel,
        EventSubAuthorizationContext authorization,
        BotAccount account,
        string sessionId,
        ChatIdentityResolution.Resolved resolved,
        CancellationToken ct
    )
    {
        var ids = new List<string>();
        try
        {
            var context = new HelixRequestContext(settings.Identity.ClientId, account.AccessToken);
            ids.Add(
                await eventSub.CreateShoutoutCreateSubscriptionAsync(
                    context,
                    resolved.BroadcasterId,
                    resolved.BotUserId,
                    sessionId,
                    ct
                )
            );
            ids.Add(
                await eventSub.CreateShoutoutReceiveSubscriptionAsync(
                    context,
                    resolved.BroadcasterId,
                    resolved.BotUserId,
                    sessionId,
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

    private async ValueTask<EventSubSubscriptionSetupOutcome> CreatePollSubscriptionsAsync(
        string channel,
        EventSubAuthorizationContext authorization,
        BotAccount account,
        string sessionId,
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
            var context = new HelixRequestContext(settings.Identity.ClientId, account.AccessToken);
            foreach (
                var type in new[]
                {
                    "channel.poll.begin",
                    "channel.poll.progress",
                    "channel.poll.end",
                }
            )
            {
                ids.Add(
                    await eventSub.CreatePollSubscriptionAsync(
                        context,
                        type,
                        broadcasterId,
                        sessionId,
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

    public ValueTask NotifyChannelStartedAsync(string channel, CancellationToken ct)
    {
        return new(lifecycle.ChannelStartedAsync(channel, ct));
    }

    public async ValueTask<EventSubSubscriptionDeletionOutcome> DeleteSubscriptionAsync(
        ActiveEventSubSubscription subscription,
        CancellationToken ct
    )
    {
        try
        {
            if (subscription.Authorization is EventSubAuthorizationContext.Broadcaster)
            {
                await DeleteBroadcasterGroupAsync(subscription.Channel, subscription, ct);
                return new EventSubSubscriptionDeletionOutcome.Deleted();
            }
            var context = new HelixRequestContext(
                settings.Identity.ClientId,
                subscription.AccessToken
            );
            await eventSub.DeleteSubscriptionAsync(context, subscription.SubscriptionId, ct);
            foreach (var id in subscription.AdditionalSubscriptionIds)
            {
                await eventSub.DeleteSubscriptionAsync(context, id, ct);
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

    private async Task DeleteBroadcasterGroupAsync(
        string channel,
        ActiveEventSubSubscription subscription,
        CancellationToken ct
    )
    {
        var broadcaster = await ResolveAccount(
                channel,
                EventSubAuthorizationContext.BroadcasterAuthority
            )
            .ExecuteAsync(ct);
        await broadcaster.Match(
            async account =>
            {
                var context = new HelixRequestContext(
                    settings.Identity.ClientId,
                    account.AccessToken
                );
                await eventSub.DeleteSubscriptionAsync(context, subscription.SubscriptionId, ct);
                foreach (var id in subscription.AdditionalSubscriptionIds)
                {
                    await eventSub.DeleteSubscriptionAsync(context, id, ct);
                }
            },
            reason => throw new InvalidOperationException(reason.ToString())
        );
    }

    public ValueTask CompleteStopAsync(string channel, CancellationToken ct)
    {
        return new(lifecycle.ChannelStoppedAsync(channel, ct));
    }

    private static ActiveEventSubSubscription CreateActive(
        string channel,
        EventSubAuthorizationContext authorization,
        BotAccount account,
        IReadOnlyList<string> ids
    )
    {
        return new()
        {
            Channel = channel,
            SubscriptionId = ids[0],
            AdditionalSubscriptionIds = ids.Skip(1).ToArray(),
            BotLogin = account.Login,
            Authorization = authorization,
            AccessToken = account.AccessToken,
            Readiness = EventSubSubscriptionReadiness.PendingStartupDelivery,
        };
    }
}
