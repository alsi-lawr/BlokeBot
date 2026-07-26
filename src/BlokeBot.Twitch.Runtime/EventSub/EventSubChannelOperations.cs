using BlokeBot.Functional;

namespace BlokeBot.Twitch.Runtime;

internal sealed class EventSubChannelOperations(
    BotSettings settings,
    IBotAccountProvider accounts,
    ChatIdentityResolver identities,
    EventSubClient eventSub,
    IStartupChatMessageProvider startupMessages,
    IPublicChatMessageSender sender,
    IBotChannelLifecycleNotifier lifecycle
) : IEventSubChannelOperations
{
    public IO<BotAccount, AccessTokenUnavailableReason> ResolveAccount(
        string channel,
        EventSubAuthorizationContext authorization
    )
    {
        return authorization.Match(
            _ => accounts.GetBotAccount(channel),
            _ =>
                IO<BotAccount, AccessTokenUnavailableReason>.Create(_ =>
                    ValueTask.FromResult(
                        Result<BotAccount, AccessTokenUnavailableReason>.Error(
                            AccessTokenUnavailableReason.BroadcasterAuthorizationUnavailable
                        )
                    )
                )
        );
    }

    public async ValueTask<EventSubSubscriptionSetupOutcome> CreateSubscriptionAsync(
        string channel,
        EventSubAuthorizationContext authorization,
        BotAccount account,
        string sessionId,
        CancellationToken cancellationToken
    )
    {
        var resolution = await identities.ResolveAsync(
            channel,
            account.Login,
            account.AccessToken,
            cancellationToken
        );
        return await resolution.Match(
            resolved =>
                CreateResolvedSubscriptionAsync(
                    channel,
                    authorization,
                    account,
                    sessionId,
                    resolved,
                    cancellationToken
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

    private async ValueTask<EventSubSubscriptionSetupOutcome> CreateResolvedSubscriptionAsync(
        string channel,
        EventSubAuthorizationContext authorization,
        BotAccount account,
        string sessionId,
        ChatIdentityResolution.Resolved resolved,
        CancellationToken cancellationToken
    )
    {
        var created = new List<string>();
        try
        {
            var context = new HelixRequestContext(settings.Identity.ClientId, account.AccessToken);
            var chat = await eventSub.CreateChatMessageSubscriptionAsync(
                context,
                resolved.BroadcasterId,
                resolved.BotUserId,
                sessionId,
                cancellationToken
            );
            created.Add(chat);
            created.Add(
                await eventSub.CreateShoutoutCreateSubscriptionAsync(
                    context,
                    resolved.BroadcasterId,
                    resolved.BotUserId,
                    sessionId,
                    cancellationToken
                )
            );
            created.Add(
                await eventSub.CreateShoutoutReceiveSubscriptionAsync(
                    context,
                    resolved.BroadcasterId,
                    resolved.BotUserId,
                    sessionId,
                    cancellationToken
                )
            );
            return new EventSubSubscriptionSetupOutcome.Created(CreateActive(created));
        }
        catch (Exception exception) when (created.Count > 0)
        {
            return new EventSubSubscriptionSetupOutcome.PartiallyCreated(
                CreateActive(created),
                exception
            );
        }

        ActiveEventSubSubscription CreateActive(IReadOnlyList<string> ids)
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

    public async ValueTask<EventSubStartupDeliveryOutcome> DeliverStartupMessageAsync(
        string channel,
        CancellationToken cancellationToken
    )
    {
        var startupMessage = await startupMessages.GetAsync(channel, cancellationToken);
        if (startupMessage is StartupChatMessage.Disabled)
        {
            return new EventSubStartupDeliveryOutcome.Completed();
        }

        var enabled = (StartupChatMessage.Enabled)startupMessage;
        var outcome = await sender.SendAsync(
            channel,
            enabled.Text,
            new PublicChatDeliveryDeadline.ConfiguredMaximum(),
            cancellationToken
        );
        return outcome.Match<EventSubStartupDeliveryOutcome>(
            static _ => new EventSubStartupDeliveryOutcome.Completed(),
            static _ => new EventSubStartupDeliveryOutcome.Rejected()
        );
    }

    public ValueTask NotifyChannelStartedAsync(string channel, CancellationToken cancellationToken)
    {
        return new(lifecycle.ChannelStartedAsync(channel, cancellationToken));
    }

    public async ValueTask<EventSubSubscriptionDeletionOutcome> DeleteSubscriptionAsync(
        ActiveEventSubSubscription subscription,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var context = new HelixRequestContext(
                settings.Identity.ClientId,
                subscription.AccessToken
            );
            await eventSub.DeleteSubscriptionAsync(
                context,
                subscription.SubscriptionId,
                cancellationToken
            );
            foreach (var subscriptionId in subscription.AdditionalSubscriptionIds)
            {
                await eventSub.DeleteSubscriptionAsync(context, subscriptionId, cancellationToken);
            }
            return new EventSubSubscriptionDeletionOutcome.Deleted();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
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
                    cancellationToken
                ),
            };
        }
    }

    public ValueTask CompleteStopAsync(string channel, CancellationToken cancellationToken)
    {
        return new(lifecycle.ChannelStoppedAsync(channel, cancellationToken));
    }
}
