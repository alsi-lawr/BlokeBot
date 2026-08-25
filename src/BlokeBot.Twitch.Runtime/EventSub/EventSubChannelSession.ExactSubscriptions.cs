using System.Diagnostics;

namespace BlokeBot.Twitch.Runtime;

internal sealed partial class EventSubChannelSession
{
    private async Task<(
        ActiveEventSubSubscription Subscription,
        EventSubChannelReconciliationOutcome? Outcome
    )> ReconcileExactSubscriptionsAsync(
        string channel,
        ActiveEventSubSubscription active,
        EventSubChannelAttemptContext context,
        CancellationToken cancellationToken
    )
    {
        var desired = (
            await operations.GetExactRequirementsAsync(channel, cancellationToken)
        ).ToHashSet();
        foreach (
            var subscription in active
                .ExactSubscriptions.Keys.Except(desired)
                .OrderBy(static item => item.Type, StringComparer.Ordinal)
                .ThenBy(static item => item.Version, StringComparer.Ordinal)
                .ToArray()
        )
        {
            var removal = await EnsureExactSubscriptionAbsentAsync(
                active,
                subscription,
                retainChannelDeletionEvidence: false,
                context,
                cancellationToken
            );
            active = removal.Subscription;
            if (removal.Outcome is { } failure)
            {
                return (active, failure);
            }
        }

        foreach (
            var subscription in desired
                .OrderBy(static item => item.Type, StringComparer.Ordinal)
                .ThenBy(static item => item.Version, StringComparer.Ordinal)
        )
        {
            var setup = await EnsureExactSubscriptionPresentAsync(
                channel,
                active,
                subscription,
                context,
                cancellationToken
            );
            active = setup.Subscription;
            if (setup.Outcome is { } failure)
            {
                return (active, failure);
            }
        }

        return (active, null);
    }

    private async Task<(
        ActiveEventSubSubscription Subscription,
        EventSubChannelReconciliationOutcome? Outcome
    )> EnsureExactSubscriptionPresentAsync(
        string channel,
        ActiveEventSubSubscription active,
        EventSubExactSubscription subscription,
        EventSubChannelAttemptContext context,
        CancellationToken cancellationToken
    )
    {
        var state = active.ExactSubscriptions.GetValueOrDefault(subscription);
        if (state is EventSubOperationSubscriptionState.Active)
        {
            return (active, null);
        }
        if (state is EventSubOperationSubscriptionState.CleanupPending)
        {
            var cleanup = await EnsureExactSubscriptionAbsentAsync(
                active,
                subscription,
                retainChannelDeletionEvidence: false,
                context,
                cancellationToken
            );
            active = cleanup.Subscription;
            if (cleanup.Outcome is { } failure)
            {
                return (active, failure);
            }
        }

        var account = await operations
            .ResolveAccount(channel, EventSubAuthorizationContext.ConfiguredBotAuthority)
            .ExecuteAsync(cancellationToken);
        return await account.Match<
            Task<(
                ActiveEventSubSubscription Subscription,
                EventSubChannelReconciliationOutcome? Outcome
            )>
        >(
            async resolved =>
            {
                var setup = await RunPhaseAsync(
                    context,
                    EventSubChannelPhase.SubscriptionSetup,
                    token =>
                        operations.CreateExactSubscriptionAsync(
                            channel,
                            resolved,
                            subscription,
                            token
                        ),
                    cancellationToken
                );
                switch (setup)
                {
                    case EventSubSubscriptionSetupOutcome.Created created:
                        return (
                            WithExactState(
                                active,
                                subscription,
                                new EventSubOperationSubscriptionState.Active(created.Subscription)
                            ),
                            null
                        );
                    case EventSubSubscriptionSetupOutcome.PartiallyCreated partial:
                        var pending = WithExactState(
                            active,
                            subscription,
                            new EventSubOperationSubscriptionState.CleanupPending(
                                partial.Subscription
                            )
                        );
                        ReplaceTrackedSubscription(pending);
                        var cleanup = await EnsureExactSubscriptionAbsentAsync(
                            pending,
                            subscription,
                            retainChannelDeletionEvidence: false,
                            context,
                            cancellationToken
                        );
                        if (cleanup.Outcome is { } cleanupFailure)
                        {
                            return (cleanup.Subscription, cleanupFailure);
                        }
                        throw new EventSubChannelOperationException(
                            EventSubChannelPhase.SubscriptionSetup,
                            partial.Failure
                        );
                    case EventSubSubscriptionSetupOutcome.MissingChannel:
                        return (active, new EventSubChannelReconciliationOutcome.MissingChannel());
                    case EventSubSubscriptionSetupOutcome.MissingBot:
                        return (active, new EventSubChannelReconciliationOutcome.MissingBot());
                    default:
                        throw new UnreachableException(
                            "Unknown exact EventSub subscription setup outcome."
                        );
                }
            },
            reason =>
                Task.FromResult(
                    (
                        active,
                        (EventSubChannelReconciliationOutcome?)
                            new EventSubChannelReconciliationOutcome.TokenUnavailable(reason)
                    )
                )
        );
    }

    private async Task<(
        ActiveEventSubSubscription Subscription,
        EventSubChannelReconciliationOutcome? Outcome
    )> EnsureExactSubscriptionAbsentAsync(
        ActiveEventSubSubscription active,
        EventSubExactSubscription key,
        bool retainChannelDeletionEvidence,
        EventSubChannelAttemptContext context,
        CancellationToken cancellationToken
    )
    {
        var exact = active.ExactSubscriptions.GetValueOrDefault(key) switch
        {
            EventSubOperationSubscriptionState.Active value => value.Subscription,
            EventSubOperationSubscriptionState.CleanupPending value => value.Subscription,
            _ => null,
        };
        if (exact is null)
        {
            var empty = WithExactState(
                active,
                key,
                new EventSubOperationSubscriptionState.NotConfigured()
            );
            TrackOperationState(empty, retainChannelDeletionEvidence);
            return (empty, null);
        }

        var pending = WithExactState(
            active,
            key,
            new EventSubOperationSubscriptionState.CleanupPending(exact)
        );
        TrackOperationState(pending, retainChannelDeletionEvidence);
        var outcome = await RunPhaseAsync(
            context,
            EventSubChannelPhase.SubscriptionDeletion,
            token => operations.DeleteSubscriptionAsync(exact, token),
            cancellationToken
        );
        if (outcome is EventSubSubscriptionDeletionOutcome.Unresolved unresolved)
        {
            if (retainChannelDeletionEvidence)
            {
                pendingDeletions.RetainUnresolved(pending, unresolved.Failure);
            }
            return (
                pending,
                new EventSubChannelReconciliationOutcome.UnresolvedDeletion
                {
                    Failure = unresolved.Failure,
                }
            );
        }

        var cleared = WithExactState(
            pending,
            key,
            new EventSubOperationSubscriptionState.NotConfigured()
        );
        TrackOperationState(cleared, retainChannelDeletionEvidence);
        return (cleared, null);
    }

    private async Task<(
        ActiveEventSubSubscription Subscription,
        EventSubChannelReconciliationOutcome? Outcome
    )> DeleteExactSubscriptionsAsync(
        ActiveEventSubSubscription active,
        EventSubChannelAttemptContext context,
        CancellationToken cancellationToken
    )
    {
        foreach (var key in active.ExactSubscriptions.Keys.ToArray())
        {
            var removal = await EnsureExactSubscriptionAbsentAsync(
                active,
                key,
                retainChannelDeletionEvidence: true,
                context,
                cancellationToken
            );
            active = removal.Subscription;
            if (removal.Outcome is { } failure)
            {
                return (active, failure);
            }
        }
        return (active, null);
    }

    private static ActiveEventSubSubscription WithExactState(
        ActiveEventSubSubscription active,
        EventSubExactSubscription subscription,
        EventSubOperationSubscriptionState state
    ) =>
        active with
        {
            ExactSubscriptions =
                state is EventSubOperationSubscriptionState.NotConfigured
                    ? active.ExactSubscriptions.Remove(subscription)
                    : active.ExactSubscriptions.SetItem(subscription, state),
        };
}
