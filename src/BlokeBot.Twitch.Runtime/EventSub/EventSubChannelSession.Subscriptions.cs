using System.Diagnostics;

namespace BlokeBot.Twitch.Runtime;

internal sealed partial class EventSubChannelSession
{
    private ValueTask<EventSubChannelReconciliationOutcome> ReconcileAsync(
        string channel,
        EventSubChannelReconciliationTarget target,
        EventSubChannelAttemptContext context,
        CancellationToken cancellationToken
    )
    {
        return target switch
        {
            EventSubChannelReconciliationTarget.Present => EnsurePresentAsync(
                channel,
                context,
                cancellationToken
            ),
            EventSubChannelReconciliationTarget.Absent => EnsureAbsentAsync(
                channel,
                context,
                cancellationToken
            ),
            _ => throw new UnreachableException("Unknown EventSub channel reconciliation target."),
        };
    }

    private async ValueTask<EventSubChannelReconciliationOutcome> EnsurePresentAsync(
        string channel,
        EventSubChannelAttemptContext context,
        CancellationToken cancellationToken
    )
    {
        var pendingDeletion = await ReconcilePendingDeletionAsync(
            channel,
            context,
            cancellationToken
        );
        switch (pendingDeletion)
        {
            case EventSubChannelReconciliationOutcome.Completed:
                break;
            case EventSubChannelReconciliationOutcome.UnresolvedDeletion:
                return pendingDeletion;
            default:
                throw new UnreachableException(
                    "Pending EventSub deletion produced a non-deletion reconciliation outcome."
                );
        }

        await CompletePendingStopAsync(channel, context, cancellationToken);
        ActiveEventSubSubscription? current;
        lock (_gate)
        {
            _subscriptions.TryGetValue(channel, out current);
        }
        var authorization =
            current?.Authorization ?? EventSubAuthorizationContext.ConfiguredBotAuthority;
        context.Phase = EventSubChannelPhase.AccountResolution;
        var accountResolution = await operations
            .ResolveAccount(channel, authorization)
            .ExecuteAsync(cancellationToken);
        return await accountResolution.Match<ValueTask<EventSubChannelReconciliationOutcome>>(
            async account =>
            {
                lock (_gate)
                {
                    _authorizedChannels.Add(channel);
                }

                ActiveEventSubSubscription? active;
                lock (_gate)
                {
                    _subscriptions.TryGetValue(channel, out active);
                }

                if (
                    active is not null
                    && !active.BotLogin.Equals(account.Login, StringComparison.OrdinalIgnoreCase)
                )
                {
                    var deletion = await ReconcileSubscriptionDeletionAsync(
                        active,
                        context,
                        cancellationToken
                    );
                    switch (deletion)
                    {
                        case EventSubChannelReconciliationOutcome.Completed:
                            break;
                        case EventSubChannelReconciliationOutcome.UnresolvedDeletion:
                            return deletion;
                        default:
                            throw new UnreachableException(
                                "EventSub subscription deletion produced a non-deletion reconciliation outcome."
                            );
                    }

                    await CompletePendingStopAsync(channel, context, cancellationToken);
                    active = null;
                }

                if (active is null)
                {
                    var setup = await RunPhaseAsync(
                        context,
                        EventSubChannelPhase.SubscriptionSetup,
                        token =>
                            operations.CreateSubscriptionAsync(
                                channel,
                                authorization,
                                account,
                                sessionId,
                                token
                            ),
                        cancellationToken
                    );
                    switch (setup)
                    {
                        case EventSubSubscriptionSetupOutcome.Created created:
                            active = created.Subscription;
                            break;
                        case EventSubSubscriptionSetupOutcome.MissingChannel:
                            return new EventSubChannelReconciliationOutcome.MissingChannel();
                        case EventSubSubscriptionSetupOutcome.MissingBot:
                            return new EventSubChannelReconciliationOutcome.MissingBot();
                        case EventSubSubscriptionSetupOutcome.PartiallyCreated partial:
                            lock (_gate)
                            {
                                _subscriptions[channel] = partial.Subscription;
                            }
                            var cleanup = await ReconcileSubscriptionDeletionAsync(
                                partial.Subscription,
                                context,
                                cancellationToken
                            );
                            if (cleanup is EventSubChannelReconciliationOutcome.UnresolvedDeletion)
                            {
                                return cleanup;
                            }
                            context.Phase = EventSubChannelPhase.SubscriptionSetup;
                            throw partial.Failure;
                        default:
                            throw new UnreachableException(
                                "Unknown EventSub subscription setup outcome."
                            );
                    }

                    lock (_gate)
                    {
                        _subscriptions[channel] = active;
                    }
                }

                switch (active.Readiness)
                {
                    case EventSubSubscriptionReadiness.PendingStartupDelivery:
                        var startupDelivery = await RunPhaseAsync(
                            context,
                            EventSubChannelPhase.SubscriptionSetup,
                            token => operations.DeliverStartupMessageAsync(channel, token),
                            cancellationToken
                        );
                        if (!startupDelivery.Match(static _ => true, static _ => false))
                        {
                            return new EventSubChannelReconciliationOutcome.StartupMessageRejected();
                        }

                        active = active with
                        {
                            Readiness = EventSubSubscriptionReadiness.PendingLifecycleStart,
                        };
                        lock (_gate)
                        {
                            _subscriptions[channel] = active;
                        }

                        goto case EventSubSubscriptionReadiness.PendingLifecycleStart;
                    case EventSubSubscriptionReadiness.PendingLifecycleStart:
                        await RunPhaseAsync(
                            context,
                            EventSubChannelPhase.SubscriptionSetup,
                            token => operations.NotifyChannelStartedAsync(channel, token),
                            cancellationToken
                        );
                        active = active with { Readiness = EventSubSubscriptionReadiness.Ready };
                        lock (_gate)
                        {
                            _subscriptions[channel] = active;
                        }

                        break;
                    case EventSubSubscriptionReadiness.Ready:
                        break;
                    default:
                        throw new UnreachableException(
                            "Unknown EventSub subscription setup stage."
                        );
                }

                context.Phase = EventSubChannelPhase.Reconciliation;
                return new EventSubChannelReconciliationOutcome.Completed();
            },
            reason =>
                ValueTask.FromResult<EventSubChannelReconciliationOutcome>(
                    new EventSubChannelReconciliationOutcome.TokenUnavailable(reason)
                )
        );
    }

    private async ValueTask<EventSubChannelReconciliationOutcome> EnsureAbsentAsync(
        string channel,
        EventSubChannelAttemptContext context,
        CancellationToken cancellationToken
    )
    {
        var pendingDeletion = await ReconcilePendingDeletionAsync(
            channel,
            context,
            cancellationToken
        );
        switch (pendingDeletion)
        {
            case EventSubChannelReconciliationOutcome.Completed:
                break;
            case EventSubChannelReconciliationOutcome.UnresolvedDeletion:
                return pendingDeletion;
            default:
                throw new UnreachableException(
                    "Pending EventSub deletion produced a non-deletion reconciliation outcome."
                );
        }

        await CompletePendingStopAsync(channel, context, cancellationToken);
        ActiveEventSubSubscription? active;
        lock (_gate)
        {
            _subscriptions.TryGetValue(channel, out active);
        }

        if (active is not null)
        {
            var deletion = await ReconcileSubscriptionDeletionAsync(
                active,
                context,
                cancellationToken
            );
            switch (deletion)
            {
                case EventSubChannelReconciliationOutcome.Completed:
                    break;
                case EventSubChannelReconciliationOutcome.UnresolvedDeletion:
                    return deletion;
                default:
                    throw new UnreachableException(
                        "EventSub subscription deletion produced a non-deletion reconciliation outcome."
                    );
            }

            await CompletePendingStopAsync(channel, context, cancellationToken);
        }

        lock (_gate)
        {
            _authorizedChannels.Remove(channel);
        }

        context.Phase = EventSubChannelPhase.Reconciliation;
        return new EventSubChannelReconciliationOutcome.Completed();
    }

    private async ValueTask<EventSubChannelReconciliationOutcome> ReconcilePendingDeletionAsync(
        string channel,
        EventSubChannelAttemptContext context,
        CancellationToken cancellationToken
    )
    {
        if (!pendingDeletions.TryGet(channel, out var pending))
        {
            return new EventSubChannelReconciliationOutcome.Completed();
        }

        return await ReconcileSubscriptionDeletionAsync(
            pending.Subscription,
            context,
            cancellationToken
        );
    }

    private async ValueTask<EventSubChannelReconciliationOutcome> ReconcileSubscriptionDeletionAsync(
        ActiveEventSubSubscription subscription,
        EventSubChannelAttemptContext context,
        CancellationToken cancellationToken
    )
    {
        pendingDeletions.Begin(subscription);
        var outcome = await RunPhaseAsync(
            context,
            EventSubChannelPhase.SubscriptionDeletion,
            token => operations.DeleteSubscriptionAsync(subscription, token),
            cancellationToken
        );
        switch (outcome)
        {
            case EventSubSubscriptionDeletionOutcome.Deleted:
                lock (_gate)
                {
                    if (
                        _subscriptions.TryGetValue(subscription.Channel, out var active)
                        && !active.SubscriptionId.Equals(
                            subscription.SubscriptionId,
                            StringComparison.Ordinal
                        )
                    )
                    {
                        throw new UnreachableException(
                            "An EventSub subscription changed while its deletion was pending."
                        );
                    }

                    pendingDeletions.ConfirmDeleted(subscription);
                    _subscriptions.Remove(subscription.Channel);
                }
                return new EventSubChannelReconciliationOutcome.Completed();
            case EventSubSubscriptionDeletionOutcome.Unresolved unresolved:
                pendingDeletions.RetainUnresolved(subscription, unresolved.Failure);
                return new EventSubChannelReconciliationOutcome.UnresolvedDeletion
                {
                    Failure = unresolved.Failure,
                };
            default:
                throw new UnreachableException("Unknown EventSub subscription-deletion outcome.");
        }
    }

    private async ValueTask CompletePendingStopAsync(
        string channel,
        EventSubChannelAttemptContext context,
        CancellationToken cancellationToken
    )
    {
        if (!pendingDeletions.HasPendingStop(channel))
        {
            return;
        }

        await RunPhaseAsync(
            context,
            EventSubChannelPhase.Reconciliation,
            token => operations.CompleteStopAsync(channel, token),
            cancellationToken
        );
        pendingDeletions.ConfirmStopped(channel);
    }

    private static async ValueTask<T> RunPhaseAsync<T>(
        EventSubChannelAttemptContext context,
        EventSubChannelPhase phase,
        Func<CancellationToken, ValueTask<T>> operation,
        CancellationToken cancellationToken
    )
    {
        context.Phase = phase;
        try
        {
            return await operation(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new EventSubChannelOperationException(phase, exception);
        }
    }

    private static async ValueTask RunPhaseAsync(
        EventSubChannelAttemptContext context,
        EventSubChannelPhase phase,
        Func<CancellationToken, ValueTask> operation,
        CancellationToken cancellationToken
    )
    {
        context.Phase = phase;
        try
        {
            await operation(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new EventSubChannelOperationException(phase, exception);
        }
    }
}
