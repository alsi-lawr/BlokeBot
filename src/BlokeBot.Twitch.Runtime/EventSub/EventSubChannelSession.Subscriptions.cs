using System.Diagnostics;

namespace BlokeBot.Twitch.Runtime;

internal sealed partial class EventSubChannelSession
{
    private static readonly EventSubOperationSubscriptionKind[] _operationSubscriptionKinds =
    [
        EventSubOperationSubscriptionKind.Shoutouts,
        EventSubOperationSubscriptionKind.Raids,
        EventSubOperationSubscriptionKind.OutgoingRaids,
        EventSubOperationSubscriptionKind.Polls,
        EventSubOperationSubscriptionKind.RewardRedemptions,
        EventSubOperationSubscriptionKind.Predictions,
        EventSubOperationSubscriptionKind.AutomationStream,
        EventSubOperationSubscriptionKind.AutomationChannelUpdates,
        EventSubOperationSubscriptionKind.AutomationFollows,
        EventSubOperationSubscriptionKind.AutomationSubscriptions,
        EventSubOperationSubscriptionKind.AutomationCheers,
        EventSubOperationSubscriptionKind.AutomationHypeTrain,
        EventSubOperationSubscriptionKind.AutomationChatNotifications,
    ];

    private ValueTask<EventSubChannelReconciliationOutcome> ReconcileAsync(
        string channel,
        EventSubChannelReconciliationTarget target,
        BotChannelTarget runtimeTarget,
        EventSubChannelAttemptContext context,
        CancellationToken cancellationToken
    ) =>
        target switch
        {
            EventSubChannelReconciliationTarget.Present => EnsurePresentAsync(
                runtimeTarget,
                context,
                cancellationToken
            ),
            EventSubChannelReconciliationTarget.Absent => EnsureAbsentAsync(
                channel,
                runtimeTarget,
                EventSubChannelDeletionLifecycle.StopRuntime,
                context,
                cancellationToken
            ),
            EventSubChannelReconciliationTarget.Replacing => ReplaceAsync(
                runtimeTarget,
                context,
                cancellationToken
            ),
            _ => throw new UnreachableException("Unknown EventSub channel reconciliation target."),
        };

    private async ValueTask<EventSubChannelReconciliationOutcome> ReplaceAsync(
        BotChannelTarget runtimeTarget,
        EventSubChannelAttemptContext context,
        CancellationToken cancellationToken
    )
    {
        var removal = await EnsureAbsentAsync(
            runtimeTarget.Channel,
            GetRuntimeTarget(runtimeTarget.Channel),
            EventSubChannelDeletionLifecycle.PreserveRuntime,
            context,
            cancellationToken
        );
        return removal switch
        {
            EventSubChannelReconciliationOutcome.Completed => await EnsurePresentAsync(
                runtimeTarget,
                context,
                cancellationToken
            ),
            EventSubChannelReconciliationOutcome.UnresolvedDeletion => removal,
            _ => throw new UnreachableException(
                "EventSub subscription replacement produced an invalid removal outcome."
            ),
        };
    }

    private async ValueTask<EventSubChannelReconciliationOutcome> EnsurePresentAsync(
        BotChannelTarget runtimeTarget,
        EventSubChannelAttemptContext context,
        CancellationToken cancellationToken
    )
    {
        var channel = runtimeTarget.Channel;
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

        await CompletePendingDeletionLifecycleAsync(
            channel,
            EventSubChannelDeletionLifecycle.PreserveRuntime,
            context,
            cancellationToken
        );
        TrackRuntimeTarget(runtimeTarget);
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
                        runtimeTarget,
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

                    await CompletePendingDeletionLifecycleAsync(
                        channel,
                        EventSubChannelDeletionLifecycle.PreserveRuntime,
                        context,
                        cancellationToken
                    );
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
                                runtimeTarget,
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

                var exactSubscriptions = await ReconcileExactSubscriptionsAsync(
                    channel,
                    active,
                    context,
                    cancellationToken
                );
                if (exactSubscriptions.Outcome is { } exactSubscriptionFailure)
                {
                    return exactSubscriptionFailure;
                }

                active = exactSubscriptions.Subscription;
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
                            token => operations.NotifyChannelStartedAsync(runtimeTarget, token),
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
        foreach (var kind in _operationSubscriptionKinds)
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
        BotChannelTarget runtimeTarget,
        EventSubChannelDeletionLifecycle lifecycle,
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

        await CompletePendingDeletionLifecycleAsync(channel, lifecycle, context, cancellationToken);
        ActiveEventSubSubscription? active;
        lock (_gate)
        {
            _ = _subscriptions.TryGetValue(channel, out active);
        }

        if (active is not null)
        {
            var deletion = await ReconcileSubscriptionDeletionAsync(
                active,
                runtimeTarget,
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

            await CompletePendingDeletionLifecycleAsync(
                channel,
                lifecycle,
                context,
                cancellationToken
            );
        }

        lock (_gate)
        {
            _ = _authorizedChannels.Remove(channel);
        }
        ForgetRuntimeTarget(runtimeTarget);

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
                pending.RuntimeTarget,
                context,
                cancellationToken
            );

    private async ValueTask<EventSubChannelReconciliationOutcome> ReconcileSubscriptionDeletionAsync(
        ActiveEventSubSubscription subscription,
        BotChannelTarget runtimeTarget,
        EventSubChannelAttemptContext context,
        CancellationToken cancellationToken
    )
    {
        pendingDeletions.Begin(subscription, runtimeTarget);
        foreach (var kind in _operationSubscriptionKinds)
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

        var exactDeletion = await DeleteExactSubscriptionsAsync(
            subscription,
            context,
            cancellationToken
        );
        subscription = exactDeletion.Subscription;
        if (exactDeletion.Outcome is { } exactFailure)
        {
            return exactFailure;
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
            EventSubOperationSubscriptionKind.OutgoingRaids =>
                subscription.OutgoingRaidSubscriptions,
            EventSubOperationSubscriptionKind.Polls => subscription.PollSubscriptions,
            EventSubOperationSubscriptionKind.RewardRedemptions =>
                subscription.RewardRedemptionSubscriptions,
            EventSubOperationSubscriptionKind.Predictions => subscription.PredictionSubscriptions,
            EventSubOperationSubscriptionKind.AutomationStream =>
                subscription.AutomationStreamSubscriptions,
            EventSubOperationSubscriptionKind.AutomationChannelUpdates =>
                subscription.AutomationChannelUpdateSubscriptions,
            EventSubOperationSubscriptionKind.AutomationFollows =>
                subscription.AutomationFollowSubscriptions,
            EventSubOperationSubscriptionKind.AutomationSubscriptions =>
                subscription.AutomationSubscriberSubscriptions,
            EventSubOperationSubscriptionKind.AutomationCheers =>
                subscription.AutomationCheerSubscriptions,
            EventSubOperationSubscriptionKind.AutomationHypeTrain =>
                subscription.AutomationHypeTrainSubscriptions,
            EventSubOperationSubscriptionKind.AutomationChatNotifications =>
                subscription.AutomationChatNotificationSubscriptions,
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
            EventSubOperationSubscriptionKind.OutgoingRaids => subscription with
            {
                OutgoingRaidSubscriptions = state,
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
            EventSubOperationSubscriptionKind.AutomationStream => subscription with
            {
                AutomationStreamSubscriptions = state,
            },
            EventSubOperationSubscriptionKind.AutomationChannelUpdates => subscription with
            {
                AutomationChannelUpdateSubscriptions = state,
            },
            EventSubOperationSubscriptionKind.AutomationFollows => subscription with
            {
                AutomationFollowSubscriptions = state,
            },
            EventSubOperationSubscriptionKind.AutomationSubscriptions => subscription with
            {
                AutomationSubscriberSubscriptions = state,
            },
            EventSubOperationSubscriptionKind.AutomationCheers => subscription with
            {
                AutomationCheerSubscriptions = state,
            },
            EventSubOperationSubscriptionKind.AutomationHypeTrain => subscription with
            {
                AutomationHypeTrainSubscriptions = state,
            },
            EventSubOperationSubscriptionKind.AutomationChatNotifications => subscription with
            {
                AutomationChatNotificationSubscriptions = state,
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
            EventSubOperationSubscriptionKind.OutgoingRaids =>
                EventSubAuthorizationContext.ConfiguredBotAuthority,
            EventSubOperationSubscriptionKind.Polls =>
                EventSubAuthorizationContext.BroadcasterAuthority,
            EventSubOperationSubscriptionKind.RewardRedemptions =>
                EventSubAuthorizationContext.RewardRedemptionsAuthority,
            EventSubOperationSubscriptionKind.Predictions =>
                EventSubAuthorizationContext.PredictionsAuthority,
            EventSubOperationSubscriptionKind.AutomationStream
            or EventSubOperationSubscriptionKind.AutomationChannelUpdates
            or EventSubOperationSubscriptionKind.AutomationFollows
            or EventSubOperationSubscriptionKind.AutomationChatNotifications =>
                EventSubAuthorizationContext.ConfiguredBotOperationsAuthority,
            EventSubOperationSubscriptionKind.AutomationSubscriptions =>
                EventSubAuthorizationContext.AutomationSubscriptionsAuthority,
            EventSubOperationSubscriptionKind.AutomationCheers =>
                EventSubAuthorizationContext.AutomationCheersAuthority,
            EventSubOperationSubscriptionKind.AutomationHypeTrain =>
                EventSubAuthorizationContext.AutomationHypeTrainAuthority,
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
            EventSubOperationSubscriptionKind.OutgoingRaids =>
                AccessTokenUnavailableReason.MissingRefreshToken,
            EventSubOperationSubscriptionKind.Polls =>
                AccessTokenUnavailableReason.BroadcasterAuthorizationUnavailable,
            EventSubOperationSubscriptionKind.RewardRedemptions =>
                AccessTokenUnavailableReason.BroadcasterAuthorizationUnavailable,
            EventSubOperationSubscriptionKind.Predictions =>
                AccessTokenUnavailableReason.BroadcasterAuthorizationUnavailable,
            EventSubOperationSubscriptionKind.AutomationStream
            or EventSubOperationSubscriptionKind.AutomationChannelUpdates
            or EventSubOperationSubscriptionKind.AutomationFollows
            or EventSubOperationSubscriptionKind.AutomationChatNotifications =>
                AccessTokenUnavailableReason.MissingRefreshToken,
            EventSubOperationSubscriptionKind.AutomationSubscriptions
            or EventSubOperationSubscriptionKind.AutomationCheers
            or EventSubOperationSubscriptionKind.AutomationHypeTrain =>
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

    private async ValueTask CompletePendingDeletionLifecycleAsync(
        string channel,
        EventSubChannelDeletionLifecycle lifecycle,
        EventSubChannelAttemptContext context,
        CancellationToken cancellationToken
    )
    {
        if (!pendingDeletions.HasPendingLifecycleReconciliation(channel))
        {
            return;
        }

        switch (lifecycle)
        {
            case EventSubChannelDeletionLifecycle.PreserveRuntime:
                break;
            case EventSubChannelDeletionLifecycle.StopRuntime:
                if (!pendingDeletions.TryGetRuntimeTarget(channel, out var runtimeTarget))
                {
                    throw new UnreachableException(
                        "A pending EventSub lifecycle stop has no runtime session identity."
                    );
                }
                await RunPhaseAsync(
                    context,
                    EventSubChannelPhase.Reconciliation,
                    token => operations.CompleteStopAsync(runtimeTarget, token),
                    cancellationToken
                );
                break;
            default:
                throw new UnreachableException("Unknown EventSub channel deletion lifecycle.");
        }

        pendingDeletions.ConfirmLifecycleReconciled(channel);
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
