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
    public IO<BotAccount, AccessTokenUnavailableReason> ResolveAccount(string channel)
    {
        return accounts.GetBotAccount(channel);
    }

    public async ValueTask<EventSubSubscriptionSetupOutcome> CreateSubscriptionAsync(
        string channel,
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
        BotAccount account,
        string sessionId,
        ChatIdentityResolution.Resolved resolved,
        CancellationToken cancellationToken
    )
    {
        return new EventSubSubscriptionSetupOutcome.Created(
            new ActiveEventSubSubscription
            {
                Channel = channel,
                SubscriptionId = await eventSub.CreateChatMessageSubscriptionAsync(
                    new HelixRequestContext(settings.Identity.ClientId, account.AccessToken),
                    resolved.BroadcasterId,
                    resolved.BotUserId,
                    sessionId,
                    cancellationToken
                ),
                AdditionalSubscriptionIds = await CreateShoutoutSubscriptionsAsync(
                    account.AccessToken,
                    resolved.BroadcasterId,
                    resolved.BotUserId,
                    sessionId,
                    cancellationToken
                ),
                BotLogin = account.Login,
                Authorization = EventSubAuthorizationContext.ConfiguredBot,
                AccessToken = account.AccessToken,
                Readiness = EventSubSubscriptionReadiness.PendingStartupDelivery,
            }
        );
    }

    private async Task<IReadOnlyList<string>> CreateShoutoutSubscriptionsAsync(
        string accessToken,
        string broadcasterId,
        string moderatorId,
        string sessionId,
        CancellationToken cancellationToken
    )
    {
        var context = new HelixRequestContext(settings.Identity.ClientId, accessToken);
        return
        [
            await eventSub.CreateShoutoutCreateSubscriptionAsync(
                context,
                broadcasterId,
                moderatorId,
                sessionId,
                cancellationToken
            ),
            await eventSub.CreateShoutoutReceiveSubscriptionAsync(
                context,
                broadcasterId,
                moderatorId,
                sessionId,
                cancellationToken
            ),
        ];
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
