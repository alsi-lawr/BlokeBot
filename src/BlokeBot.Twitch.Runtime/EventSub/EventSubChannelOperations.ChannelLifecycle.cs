namespace BlokeBot.Twitch.Runtime;

internal sealed partial class EventSubChannelOperations
{
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
}
