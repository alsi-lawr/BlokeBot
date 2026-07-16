using BlokeBot.Functional;

namespace BlokeBot.Twitch.Runtime;

internal interface IEventSubChannelOperations
{
    IO<BotAccount, AccessTokenUnavailableReason> ResolveAccount(string channel);

    ValueTask<EventSubSubscriptionSetupOutcome> CreateSubscriptionAsync(
        string channel,
        BotAccount account,
        string sessionId,
        CancellationToken cancellationToken
    );

    ValueTask<EventSubStartupDeliveryOutcome> DeliverStartupMessageAsync(
        string channel,
        CancellationToken cancellationToken
    );

    ValueTask NotifyChannelStartedAsync(string channel, CancellationToken cancellationToken);

    ValueTask<EventSubSubscriptionDeletionOutcome> DeleteSubscriptionAsync(
        ActiveEventSubSubscription subscription,
        CancellationToken cancellationToken
    );

    ValueTask CompleteStopAsync(string channel, CancellationToken cancellationToken);
}
