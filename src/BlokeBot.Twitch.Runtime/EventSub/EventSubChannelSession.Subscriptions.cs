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

                active = await EnsurePollSubscriptionsAsync(
                    channel,
                    active,
                    context,
                    cancellationToken
                );
                lock (_gate)
                {
                    _subscriptions[channel] = active;
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

    private async Task<ActiveEventSubSubscription> EnsurePollSubscriptionsAsync(
        string channel,
        ActiveEventSubSubscription active,
        EventSubChannelAttemptContext context,
        CancellationToken cancellationToken
    )
    {
        if (active.PollSubscriptions is BroadcasterPollSubscriptionState.Active)
        {
            return active;
        }

        if (active.PollSubscriptions is BroadcasterPollSubscriptionState.CleanupPending pending)
        {
            var cleanup = await RunPhaseAsync(
                context,
                EventSubChannelPhase.SubscriptionDeletion,
                token =>
                    operations.DeleteSubscriptionAsync(
                        new ActiveEventSubSubscription
                        {
                            Channel = channel,
                            SubscriptionId = pending.Group.SubscriptionId,
                            AdditionalSubscriptionIds = pending.Group.AdditionalSubscriptionIds,
                            BotLogin = active.BotLogin,
                            Authorization = EventSubAuthorizationContext.BroadcasterAuthority,
                            AccessToken = string.Empty,
                            Readiness = EventSubSubscriptionReadiness.Ready,
                        },
                        token
                    ),
                cancellationToken
            );
            if (cleanup is EventSubSubscriptionDeletionOutcome.Unresolved unresolved)
            {
                throw new EventSubChannelOperationException(
                    EventSubChannelPhase.SubscriptionDeletion,
                    unresolved.Failure.Exception
                        ?? new InvalidOperationException(unresolved.Failure.FailureType)
                );
            }
            active = active with
            {
                PollSubscriptions = new BroadcasterPollSubscriptionState.NotConfigured(),
            };
        }

        var account = await operations
            .ResolveAccount(channel, EventSubAuthorizationContext.BroadcasterAuthority)
            .ExecuteAsync(cancellationToken);
        return await account.Match<Task<ActiveEventSubSubscription>>(
            async broadcaster =>
            {
                var setup = await RunPhaseAsync(
                    context,
                    EventSubChannelPhase.SubscriptionSetup,
                    token =>
                        operations.CreateSubscriptionAsync(
                            channel,
                            EventSubAuthorizationContext.BroadcasterAuthority,
                            broadcaster,
                            sessionId,
                            token
                        ),
                    cancellationToken
                );
                switch (setup)
                {
                    case EventSubSubscriptionSetupOutcome.Created created:
                        return active with
                        {
                            PollSubscriptions = new BroadcasterPollSubscriptionState.Active(
                                BroadcasterPollSubscriptionGroup.From(created.Subscription)
                            ),
                        };
                    case EventSubSubscriptionSetupOutcome.PartiallyCreated partial:
                        var group = BroadcasterPollSubscriptionGroup.From(partial.Subscription);
                        var pending = active with
                        {
                            PollSubscriptions = new BroadcasterPollSubscriptionState.CleanupPending(
                                group
                            ),
                        };
                        lock (_gate)
                        {
                            _subscriptions[channel] = pending;
                        }

                        var cleanup = await RunPhaseAsync(
                            context,
                            EventSubChannelPhase.SubscriptionDeletion,
                            token =>
                                operations.DeleteSubscriptionAsync(partial.Subscription, token),
                            cancellationToken
                        );
                        if (cleanup is EventSubSubscriptionDeletionOutcome.Unresolved unresolved)
                        {
                            throw new EventSubChannelOperationException(
                                EventSubChannelPhase.SubscriptionDeletion,
                                unresolved.Failure.Exception
                                    ?? new InvalidOperationException(unresolved.Failure.FailureType)
                            );
                        }

                        throw new EventSubChannelOperationException(
                            EventSubChannelPhase.SubscriptionSetup,
                            partial.Failure
                        );
                    case EventSubSubscriptionSetupOutcome.MissingChannel:
                    case EventSubSubscriptionSetupOutcome.MissingBot:
                        return active with
                        {
                            PollSubscriptions = new BroadcasterPollSubscriptionState.Unavailable(
                                AccessTokenUnavailableReason.BroadcasterAuthorizationUnavailable
                            ),
                        };
                    default:
                        throw new UnreachableException("Unknown poll EventSub setup outcome.");
                }
            },
            reason =>
                Task.FromResult(
                    active with
                    {
                        PollSubscriptions = new BroadcasterPollSubscriptionState.Unavailable(
                            reason
                        ),
                    }
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
        var pollDeletion = await ReconcilePollSubscriptionDeletionAsync(
            subscription,
            context,
            cancellationToken
        );
        if (
            pollDeletion.Outcome
            is EventSubChannelReconciliationOutcome.UnresolvedDeletion unresolvedPoll
        )
        {
            return unresolvedPoll;
        }

        subscription = pollDeletion.Subscription;
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

    private async Task<(
        ActiveEventSubSubscription Subscription,
        EventSubChannelReconciliationOutcome? Outcome
    )> ReconcilePollSubscriptionDeletionAsync(
        ActiveEventSubSubscription subscription,
        EventSubChannelAttemptContext context,
        CancellationToken cancellationToken
    )
    {
        var group = subscription.PollSubscriptions switch
        {
            BroadcasterPollSubscriptionState.Active active => active.Group,
            BroadcasterPollSubscriptionState.CleanupPending pending => pending.Group,
            _ => null,
        };
        if (group is null)
        {
            return (subscription, null);
        }

        var outcome = await RunPhaseAsync(
            context,
            EventSubChannelPhase.SubscriptionDeletion,
            token =>
                operations.DeleteSubscriptionAsync(
                    new ActiveEventSubSubscription
                    {
                        Channel = subscription.Channel,
                        SubscriptionId = group.SubscriptionId,
                        AdditionalSubscriptionIds = group.AdditionalSubscriptionIds,
                        BotLogin = subscription.BotLogin,
                        Authorization = EventSubAuthorizationContext.BroadcasterAuthority,
                        AccessToken = string.Empty,
                        Readiness = EventSubSubscriptionReadiness.Ready,
                    },
                    token
                ),
            cancellationToken
        );
        if (outcome is EventSubSubscriptionDeletionOutcome.Unresolved unresolved)
        {
            var pending = subscription with
            {
                PollSubscriptions = new BroadcasterPollSubscriptionState.CleanupPending(group),
            };
            pendingDeletions.RetainUnresolved(pending, unresolved.Failure);
            ReplaceTrackedSubscription(pending);
            return (
                pending,
                new EventSubChannelReconciliationOutcome.UnresolvedDeletion
                {
                    Failure = unresolved.Failure,
                }
            );
        }

        var cleared = subscription with
        {
            PollSubscriptions = new BroadcasterPollSubscriptionState.NotConfigured(),
        };
        pendingDeletions.UpdateSubscription(cleared);
        ReplaceTrackedSubscription(cleared);
        return (cleared, null);
    }

    private void ReplaceTrackedSubscription(ActiveEventSubSubscription subscription)
    {
        lock (_gate)
        {
            if (
                _subscriptions.TryGetValue(subscription.Channel, out var active)
                && active.SubscriptionId.Equals(
                    subscription.SubscriptionId,
                    StringComparison.Ordinal
                )
            )
            {
                _subscriptions[subscription.Channel] = subscription;
            }
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
