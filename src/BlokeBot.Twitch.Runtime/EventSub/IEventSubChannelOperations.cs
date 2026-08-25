using BlokeBot.Functional;

namespace BlokeBot.Twitch.Runtime;

internal interface IEventSubChannelOperations
{
    IO<BotAccount, AccessTokenUnavailableReason> ResolveAccount(
        string channel,
        EventSubAuthorizationContext authorization
    );

    ValueTask<EventSubSubscriptionSetupOutcome> CreateSubscriptionAsync(
        string channel,
        EventSubAuthorizationContext authorization,
        BotAccount account,
        CancellationToken cancellationToken,
        EventSubOperationSubscriptionKind? operationKind = null
    );

    ValueTask<IReadOnlyList<EventSubExactSubscription>> GetExactRequirementsAsync(
        string channel,
        CancellationToken cancellationToken
    );

    ValueTask<EventSubSubscriptionSetupOutcome> CreateExactSubscriptionAsync(
        string channel,
        BotAccount account,
        EventSubExactSubscription subscription,
        CancellationToken cancellationToken
    );

    ValueTask<bool> NativeTwitchFeatureIsEnabledAsync(
        string channel,
        EventSubOperationSubscriptionKind kind,
        CancellationToken cancellationToken
    );

    ValueTask<EventSubStartupDeliveryOutcome> DeliverStartupMessageAsync(
        string channel,
        CancellationToken cancellationToken
    );

    ValueTask NotifyChannelStartedAsync(
        BotChannelTarget target,
        CancellationToken cancellationToken
    );

    ValueTask<EventSubSubscriptionDeletionOutcome> DeleteSubscriptionAsync(
        ActiveEventSubSubscription subscription,
        CancellationToken cancellationToken
    );

    ValueTask CompleteStopAsync(BotChannelTarget target, CancellationToken cancellationToken);
}
