using System.Diagnostics;

namespace BlokeBot.Twitch.Runtime;

internal sealed partial class EventSubChannelSession
{
    private ValueTask<EventSubChannelReconciliationOutcome> ReconcileAsync(
        string channel,
        EventSubChannelReconciliationTarget target,
        EventSubChannelAttemptContext context,
        CancellationToken cancellationToken
    ) =>
        target switch
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
            _ = _subscriptions.TryGetValue(channel, out current);
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
                    _ = _authorizedChannels.Add(channel);
                }

                ActiveEventSubSubscription? active;
                lock (_gate)
                {
                    _ = _subscriptions.TryGetValue(channel, out active);
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

                var nativeSubscriptions = await ReconcileNativeSubscriptionsAsync(
                    channel,
                    active,
                    context,
                    cancellationToken
                );
                if (nativeSubscriptions.Outcome is { } nativeSubscriptionFailure)
                {
                    return nativeSubscriptionFailure;
                }

                active = nativeSubscriptions.Subscription;
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

    private async Task<(
        ActiveEventSubSubscription Subscription,
        EventSubChannelReconciliationOutcome? Outcome
    )> ReconcileNativeSubscriptionsAsync(
        string channel,
        ActiveEventSubSubscription active,
        EventSubChannelAttemptContext context,
        CancellationToken cancellationToken
    )
    {
        foreach (
            var kind in new[]
            {
                EventSubOperationSubscriptionKind.Shoutouts,
                EventSubOperationSubscriptionKind.Raids,
                EventSubOperationSubscriptionKind.Polls,
                EventSubOperationSubscriptionKind.RewardRedemptions,
                EventSubOperationSubscriptionKind.Predictions,
            }
        )
        {
            var enabled = await operations.NativeTwitchFeatureIsEnabledAsync(
                channel,
                kind,
                cancellationToken
            );
            var reconciliation = enabled
                ? await EnsureOperationSubscriptionPresentAsync(
                    channel,
                    active,
                    kind,
                    context,
                    cancellationToken
                )
                : await EnsureOperationSubscriptionAbsentAsync(
                    active,
                    kind,
                    retainChannelDeletionEvidence: false,
                    context,
                    cancellationToken
                );
            active = reconciliation.Subscription;
            ReplaceTrackedSubscription(active);
            if (reconciliation.Outcome is { } failure)
            {
                return (active, failure);
            }
        }

        return (active, null);
    }

    private async Task<(
        ActiveEventSubSubscription Subscription,
        EventSubChannelReconciliationOutcome? Outcome
    )> EnsureOperationSubscriptionPresentAsync(
        string channel,
        ActiveEventSubSubscription active,
        EventSubOperationSubscriptionKind kind,
        EventSubChannelAttemptContext context,
        CancellationToken cancellationToken
    )
    {
        var state = GetOperationState(active, kind);
        if (state is EventSubOperationSubscriptionState.Active)
        {
            return (active, null);
        }

        if (state is EventSubOperationSubscriptionState.CleanupPending)
        {
            var cleanup = await EnsureOperationSubscriptionAbsentAsync(
                active,
                kind,
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

        var authorization = AuthorizationFor(kind);
        var account = await operations
            .ResolveAccount(channel, authorization)
            .ExecuteAsync(cancellationToken);
        return await account.Match<
            Task<(
                ActiveEventSubSubscription Subscription,
                EventSubChannelReconciliationOutcome? Outcome
            )>
        >(
            async resolvedAccount =>
            {
                var setup = await RunPhaseAsync(
                    context,
                    EventSubChannelPhase.SubscriptionSetup,
                    token =>
                        operations.CreateSubscriptionAsync(
                            channel,
                            authorization,
                            resolvedAccount,
                            token,
                            kind
                        ),
                    cancellationToken
                );
                switch (setup)
                {
                    case EventSubSubscriptionSetupOutcome.Created created:
                        return (
                            WithOperationState(
                                active,
                                kind,
                                new EventSubOperationSubscriptionState.Active(created.Subscription)
                            ),
                            null
                        );
                    case EventSubSubscriptionSetupOutcome.PartiallyCreated partial:
                        var pending = WithOperationState(
                            active,
                            kind,
                            new EventSubOperationSubscriptionState.CleanupPending(
                                partial.Subscription
                            )
                        );
                        ReplaceTrackedSubscription(pending);
                        var cleanup = await EnsureOperationSubscriptionAbsentAsync(
                            pending,
                            kind,
                            retainChannelDeletionEvidence: false,
                            context,
                            cancellationToken
                        );
                        if (cleanup.Outcome is { } failure)
                        {
                            return (cleanup.Subscription, failure);
                        }

                        throw new EventSubChannelOperationException(
                            EventSubChannelPhase.SubscriptionSetup,
                            partial.Failure
                        );
                    case EventSubSubscriptionSetupOutcome.MissingChannel:
                    case EventSubSubscriptionSetupOutcome.MissingBot:
                        return (
                            WithOperationState(
                                active,
                                kind,
                                new EventSubOperationSubscriptionState.Unavailable(
                                    UnavailableReasonFor(kind)
                                )
                            ),
                            null
                        );
                    default:
                        throw new UnreachableException(
                            "Unknown Native Twitch EventSub setup outcome."
                        );
                }
            },
            reason =>
                Task.FromResult(
                    (
                        WithOperationState(
                            active,
                            kind,
                            new EventSubOperationSubscriptionState.Unavailable(reason)
                        ),
                        (EventSubChannelReconciliationOutcome?)null
                    )
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
            _ = _subscriptions.TryGetValue(channel, out active);
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
            _ = _authorizedChannels.Remove(channel);
        }

        context.Phase = EventSubChannelPhase.Reconciliation;
        return new EventSubChannelReconciliationOutcome.Completed();
    }

    private async ValueTask<EventSubChannelReconciliationOutcome> ReconcilePendingDeletionAsync(
        string channel,
        EventSubChannelAttemptContext context,
        CancellationToken cancellationToken
    ) =>
        !pendingDeletions.TryGet(channel, out var pending)
            ? new EventSubChannelReconciliationOutcome.Completed()
            : await ReconcileSubscriptionDeletionAsync(
                pending.Subscription,
                context,
                cancellationToken
            );

    private async ValueTask<EventSubChannelReconciliationOutcome> ReconcileSubscriptionDeletionAsync(
        ActiveEventSubSubscription subscription,
        EventSubChannelAttemptContext context,
        CancellationToken cancellationToken
    )
    {
        pendingDeletions.Begin(subscription);
        foreach (
            var kind in new[]
            {
                EventSubOperationSubscriptionKind.Shoutouts,
                EventSubOperationSubscriptionKind.Raids,
                EventSubOperationSubscriptionKind.Polls,
                EventSubOperationSubscriptionKind.RewardRedemptions,
                EventSubOperationSubscriptionKind.Predictions,
            }
        )
        {
            var operationDeletion = await EnsureOperationSubscriptionAbsentAsync(
                subscription,
                kind,
                retainChannelDeletionEvidence: true,
                context,
                cancellationToken
            );
            subscription = operationDeletion.Subscription;
            if (operationDeletion.Outcome is { } operationFailure)
            {
                return operationFailure;
            }
        }

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
                    _ = _subscriptions.Remove(subscription.Channel);
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
    )> EnsureOperationSubscriptionAbsentAsync(
        ActiveEventSubSubscription subscription,
        EventSubOperationSubscriptionKind kind,
        bool retainChannelDeletionEvidence,
        EventSubChannelAttemptContext context,
        CancellationToken cancellationToken
    )
    {
        var operationSubscription = GetOperationState(subscription, kind) switch
        {
            EventSubOperationSubscriptionState.Active active => active.Subscription,
            EventSubOperationSubscriptionState.CleanupPending cleanupPending =>
                cleanupPending.Subscription,
            _ => null,
        };
        if (operationSubscription is null)
        {
            var emptyState = WithOperationState(
                subscription,
                kind,
                new EventSubOperationSubscriptionState.NotConfigured()
            );
            TrackOperationState(emptyState, retainChannelDeletionEvidence);
            return (emptyState, null);
        }

        var pendingState = WithOperationState(
            subscription,
            kind,
            new EventSubOperationSubscriptionState.CleanupPending(operationSubscription)
        );
        TrackOperationState(pendingState, retainChannelDeletionEvidence);
        var outcome = await RunPhaseAsync(
            context,
            EventSubChannelPhase.SubscriptionDeletion,
            token => operations.DeleteSubscriptionAsync(operationSubscription, token),
            cancellationToken
        );
        if (outcome is EventSubSubscriptionDeletionOutcome.Unresolved unresolved)
        {
            if (retainChannelDeletionEvidence)
            {
                pendingDeletions.RetainUnresolved(pendingState, unresolved.Failure);
            }

            return (
                pendingState,
                new EventSubChannelReconciliationOutcome.UnresolvedDeletion
                {
                    Failure = unresolved.Failure,
                }
            );
        }

        var clearedState = WithOperationState(
            pendingState,
            kind,
            new EventSubOperationSubscriptionState.NotConfigured()
        );
        TrackOperationState(clearedState, retainChannelDeletionEvidence);
        return (clearedState, null);
    }

    private void TrackOperationState(
        ActiveEventSubSubscription subscription,
        bool retainChannelDeletionEvidence
    )
    {
        if (retainChannelDeletionEvidence)
        {
            pendingDeletions.UpdateSubscription(subscription);
        }

        ReplaceTrackedSubscription(subscription);
    }

    private static EventSubOperationSubscriptionState GetOperationState(
        ActiveEventSubSubscription subscription,
        EventSubOperationSubscriptionKind kind
    ) =>
        kind switch
        {
            EventSubOperationSubscriptionKind.Shoutouts => subscription.ShoutoutSubscriptions,
            EventSubOperationSubscriptionKind.Raids => subscription.RaidSubscriptions,
            EventSubOperationSubscriptionKind.Polls => subscription.PollSubscriptions,
            EventSubOperationSubscriptionKind.RewardRedemptions =>
                subscription.RewardRedemptionSubscriptions,
            EventSubOperationSubscriptionKind.Predictions => subscription.PredictionSubscriptions,
            _ => throw new UnreachableException(
                "Unknown Native Twitch EventSub subscription kind."
            ),
        };

    private static ActiveEventSubSubscription WithOperationState(
        ActiveEventSubSubscription subscription,
        EventSubOperationSubscriptionKind kind,
        EventSubOperationSubscriptionState state
    ) =>
        kind switch
        {
            EventSubOperationSubscriptionKind.Shoutouts => subscription with
            {
                ShoutoutSubscriptions = state,
            },
            EventSubOperationSubscriptionKind.Raids => subscription with
            {
                RaidSubscriptions = state,
            },
            EventSubOperationSubscriptionKind.Polls => subscription with
            {
                PollSubscriptions = state,
            },
            EventSubOperationSubscriptionKind.RewardRedemptions => subscription with
            {
                RewardRedemptionSubscriptions = state,
            },
            EventSubOperationSubscriptionKind.Predictions => subscription with
            {
                PredictionSubscriptions = state,
            },
            _ => throw new UnreachableException(
                "Unknown Native Twitch EventSub subscription kind."
            ),
        };

    private static EventSubAuthorizationContext AuthorizationFor(
        EventSubOperationSubscriptionKind kind
    ) =>
        kind switch
        {
            EventSubOperationSubscriptionKind.Shoutouts =>
                EventSubAuthorizationContext.ConfiguredBotOperationsAuthority,
            EventSubOperationSubscriptionKind.Raids =>
                EventSubAuthorizationContext.ConfiguredBotAuthority,
            EventSubOperationSubscriptionKind.Polls =>
                EventSubAuthorizationContext.BroadcasterAuthority,
            EventSubOperationSubscriptionKind.RewardRedemptions =>
                EventSubAuthorizationContext.RewardRedemptionsAuthority,
            EventSubOperationSubscriptionKind.Predictions =>
                EventSubAuthorizationContext.PredictionsAuthority,
            _ => throw new UnreachableException(
                "Unknown Native Twitch EventSub subscription kind."
            ),
        };

    private static AccessTokenUnavailableReason UnavailableReasonFor(
        EventSubOperationSubscriptionKind kind
    ) =>
        kind switch
        {
            EventSubOperationSubscriptionKind.Shoutouts =>
                AccessTokenUnavailableReason.MissingRefreshToken,
            EventSubOperationSubscriptionKind.Raids =>
                AccessTokenUnavailableReason.MissingRefreshToken,
            EventSubOperationSubscriptionKind.Polls =>
                AccessTokenUnavailableReason.BroadcasterAuthorizationUnavailable,
            EventSubOperationSubscriptionKind.RewardRedemptions =>
                AccessTokenUnavailableReason.BroadcasterAuthorizationUnavailable,
            EventSubOperationSubscriptionKind.Predictions =>
                AccessTokenUnavailableReason.BroadcasterAuthorizationUnavailable,
            _ => throw new UnreachableException(
                "Unknown Native Twitch EventSub subscription kind."
            ),
        };

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
