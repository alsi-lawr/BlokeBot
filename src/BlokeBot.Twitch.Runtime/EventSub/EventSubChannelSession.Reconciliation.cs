using System.Diagnostics;

namespace BlokeBot.Twitch.Runtime;

internal sealed partial class EventSubChannelSession
{
    private async Task RunReconciliationAsync(
        IReadOnlyList<string> desiredChannels,
        EventSubChannelRecoveryTrigger trigger,
        CancellationToken cancellationToken
    )
    {
        string[] trackedChannels;
        lock (_gate)
        {
            trackedChannels = _subscriptions.Keys.Union(_states.Keys).ToArray();
        }

        trackedChannels = trackedChannels
            .Union(pendingDeletions.ReconciliationChannels, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var desired = BotChannelList.Normalize(desiredChannels);
        var removed = trackedChannels.Except(desired, StringComparer.OrdinalIgnoreCase).ToArray();
        await Task.WhenAll(
            desired
                .Select(channel =>
                    RunTriggeredAsync(
                        channel,
                        EventSubChannelReconciliationTarget.Present,
                        trigger,
                        cancellationToken
                    )
                )
                .Concat(
                    removed.Select(channel =>
                        RunTriggeredAsync(
                            channel,
                            EventSubChannelReconciliationTarget.Absent,
                            trigger,
                            cancellationToken
                        )
                    )
                )
        );
    }

    private async Task RunTriggeredAsync(
        string channel,
        EventSubChannelReconciliationTarget target,
        EventSubChannelRecoveryTrigger trigger,
        CancellationToken cancellationToken
    )
    {
        EventSubChannelStatus? state;
        lock (_gate)
        {
            _states.TryGetValue(channel, out state);
        }

        if (state is EventSubChannelStatus.Degraded degraded)
        {
            if (
                target is EventSubChannelReconciliationTarget.Present
                && degraded.NextAction is EventSubChannelNextAction.NoFurtherAction
            )
            {
                return;
            }

            await RunRecoveryCycleAsync(
                channel,
                target,
                trigger,
                GetFailureContext(channel),
                cancellationToken
            );
            return;
        }

        await RunImmediateAsync(channel, target, trigger, cancellationToken);
    }

    private async Task RunImmediateAsync(
        string channel,
        EventSubChannelReconciliationTarget target,
        EventSubChannelRecoveryTrigger trigger,
        CancellationToken cancellationToken
    )
    {
        var context = new EventSubChannelAttemptContext();
        EventSubChannelReconciliationOutcome outcome;
        try
        {
            outcome = await recovery.ExecuteAttemptAsync(
                token => ReconcileAsync(channel, target, context, token),
                cancellationToken
            );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var failureDetails = EventSubChannelFailureClassifier.Classify(
                exception,
                context.Phase,
                cancellationToken
            );
            var failure = new EventSubChannelFailureContext.ClassifiedException(failureDetails);
            RetainPendingDeletionFailure(channel, failureDetails);
            var isRecoverable = EventSubChannelFailureClassifier.IsRecoverable(
                failure.Classification
            );
            PublishDegraded(
                channel,
                trigger,
                attempt: 1,
                failure,
                isRecoverable
                    ? EventSubChannelNextAction.BeginRecoveryCycle
                    : EventSubChannelNextAction.RetryOnNextReconciliation
            );

            if (isRecoverable)
            {
                await RunRecoveryCycleAsync(channel, target, trigger, failure, cancellationToken);
            }

            return;
        }

        await outcome.Match(
            PublishOutcomeAsync,
            PublishOutcomeAsync,
            PublishOutcomeAsync,
            PublishOutcomeAsync,
            PublishOutcomeAsync,
            HandleUnresolvedDeletionAsync
        );
        return;

        ValueTask PublishOutcomeAsync(EventSubChannelReconciliationOutcome result)
        {
            PublishReconciliationOutcome(channel, target, trigger, attempt: 1, result);
            return ValueTask.CompletedTask;
        }

        async ValueTask HandleUnresolvedDeletionAsync(
            EventSubChannelReconciliationOutcome.UnresolvedDeletion unresolved
        )
        {
            var failure = new EventSubChannelFailureContext.ClassifiedException(unresolved.Failure);
            var isRecoverable = EventSubChannelFailureClassifier.IsRecoverable(
                failure.Classification
            );
            PublishDegraded(
                channel,
                trigger,
                attempt: 1,
                failure,
                isRecoverable
                    ? EventSubChannelNextAction.BeginRecoveryCycle
                    : EventSubChannelNextAction.RetryOnNextReconciliation
            );
            if (isRecoverable)
            {
                await RunRecoveryCycleAsync(channel, target, trigger, failure, cancellationToken);
            }
        }
    }

    private EventSubChannelFailureContext GetFailureContext(string channel)
    {
        lock (_gate)
        {
            return _failures.TryGetValue(channel, out var failure)
                ? failure
                : throw new UnreachableException(
                    "A failed EventSub channel state has no internal failure context."
                );
        }
    }

    private void RetainPendingDeletionFailure(string channel, EventSubChannelFailureDetails failure)
    {
        if (
            failure.Phase is not EventSubChannelPhase.SubscriptionDeletion
            || failure.Classification is EventSubChannelFailureClassification.Cancellation
        )
        {
            return;
        }

        if (!pendingDeletions.TryGet(channel, out var pending))
        {
            throw new UnreachableException(
                "An unresolved EventSub deletion has no pending local evidence."
            );
        }

        pendingDeletions.RetainUnresolved(pending.Subscription, failure);
    }

    private async Task RunRecoveryCycleAsync(
        string channel,
        EventSubChannelReconciliationTarget target,
        EventSubChannelRecoveryTrigger trigger,
        EventSubChannelFailureContext initialFailure,
        CancellationToken cancellationToken
    )
    {
        var attempt = 0;
        var latestFailure = initialFailure;
        var context = new EventSubChannelAttemptContext { Phase = initialFailure.Phase };
        EventSubChannelReconciliationOutcome outcome;
        try
        {
            outcome = await recovery.ExecuteRecoveryAsync(
                async attemptToken =>
                {
                    checked
                    {
                        attempt++;
                    }

                    PublishRecovering(channel, trigger, attempt, latestFailure);
                    try
                    {
                        var outcome = await ReconcileAsync(channel, target, context, attemptToken);
                        if (
                            outcome
                            is EventSubChannelReconciliationOutcome.UnresolvedDeletion unresolved
                        )
                        {
                            latestFailure = new EventSubChannelFailureContext.ClassifiedException(
                                unresolved.Failure
                            );
                        }

                        return outcome;
                    }
                    catch (Exception exception)
                    {
                        var failureDetails = EventSubChannelFailureClassifier.Classify(
                            exception,
                            context.Phase,
                            cancellationToken
                        );
                        latestFailure = new EventSubChannelFailureContext.ClassifiedException(
                            failureDetails
                        );
                        RetainPendingDeletionFailure(channel, failureDetails);
                        throw;
                    }
                },
                cancellationToken
            );
        }
        catch (EventSubChannelStatusPublicationException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            var failureDetails = EventSubChannelFailureClassifier.Classify(
                exception,
                context.Phase,
                cancellationToken
            );
            latestFailure = new EventSubChannelFailureContext.ClassifiedException(failureDetails);
            RetainPendingDeletionFailure(channel, failureDetails);
            PublishDegraded(
                channel,
                trigger,
                attempt,
                latestFailure,
                EventSubChannelNextAction.RetryOnNextReconciliation
            );
            return;
        }

        PublishReconciliationOutcome(channel, target, trigger, attempt, outcome);
    }
}
