using System.Diagnostics;

namespace BlokeBot.Twitch.Runtime;

internal sealed partial class EventSubChannelSession
{
    private void PublishReconciliationOutcome(
        string channel,
        EventSubChannelReconciliationTarget target,
        EventSubChannelRecoveryTrigger trigger,
        int attempt,
        EventSubChannelReconciliationOutcome outcome
    )
    {
        outcome
            .Match<Action>(
                _ => () => PublishSuccess(channel, target, attempt, trigger),
                _ =>
                    () =>
                        PublishDegraded(
                            channel,
                            trigger,
                            attempt,
                            new EventSubChannelFailureContext.MissingChannel(),
                            EventSubChannelNextAction.RetryOnNextReconciliation
                        ),
                _ =>
                    () =>
                        PublishDegraded(
                            channel,
                            trigger,
                            attempt,
                            new EventSubChannelFailureContext.MissingBot(),
                            EventSubChannelNextAction.RetryOnNextReconciliation
                        ),
                _ =>
                    () =>
                        PublishDegraded(
                            channel,
                            trigger,
                            attempt,
                            new EventSubChannelFailureContext.StartupMessageRejected(),
                            EventSubChannelNextAction.NoFurtherAction
                        ),
                unavailable =>
                    () =>
                        PublishDegraded(
                            channel,
                            trigger,
                            attempt,
                            new EventSubChannelFailureContext.TokenUnavailable(unavailable.Reason),
                            EventSubChannelNextAction.RetryOnNextReconciliation
                        ),
                unresolved =>
                    () =>
                        PublishDegraded(
                            channel,
                            trigger,
                            attempt,
                            new EventSubChannelFailureContext.ClassifiedException(
                                unresolved.Failure
                            ),
                            EventSubChannelNextAction.RetryOnNextReconciliation
                        )
            )
            .Invoke();
    }

    private void PublishSuccess(
        string channel,
        EventSubChannelReconciliationTarget target,
        int attempt,
        EventSubChannelRecoveryTrigger trigger
    )
    {
        switch (target)
        {
            case EventSubChannelReconciliationTarget.Present:
                Publish(
                    new EventSubChannelDiagnosticReport.Healthy
                    {
                        ChannelStatus = new EventSubChannelStatus.Healthy
                        {
                            Channel = channel,
                            Phase = EventSubChannelPhase.Reconciliation,
                            Attempt = attempt,
                            ChangedAt = timeProvider.GetUtcNow(),
                            Trigger = trigger,
                        },
                    }
                );
                return;
            case EventSubChannelReconciliationTarget.Absent:
                lock (_gate)
                {
                    _states.Remove(channel);
                    _failures.Remove(channel);
                    statusScope.Remove(channel);
                    UpdateRuntimeStatusLocked();
                }
                return;
            default:
                throw new UnreachableException("Unknown EventSub channel reconciliation target.");
        }
    }

    private void PublishRecovering(
        string channel,
        EventSubChannelRecoveryTrigger trigger,
        int attempt,
        EventSubChannelFailureContext failure
    )
    {
        Publish(
            new EventSubChannelDiagnosticReport.Recovering
            {
                ChannelStatus = new EventSubChannelStatus.Recovering
                {
                    Channel = channel,
                    Phase = failure.Phase,
                    Attempt = attempt,
                    ChangedAt = timeProvider.GetUtcNow(),
                    Trigger = trigger,
                    Failure = failure.ToPublicFailure(),
                    NextAction = EventSubChannelNextAction.ContinueRecoveryCycle,
                },
                Failure = failure,
            }
        );
    }

    private void PublishDegraded(
        string channel,
        EventSubChannelRecoveryTrigger trigger,
        int attempt,
        EventSubChannelFailureContext failure,
        EventSubChannelNextAction nextAction
    )
    {
        if (failure.Phase is EventSubChannelPhase.AccountResolution)
        {
            lock (_gate)
            {
                _authorizedChannels.Remove(channel);
            }
        }

        Publish(
            new EventSubChannelDiagnosticReport.Degraded
            {
                ChannelStatus = new EventSubChannelStatus.Degraded
                {
                    Channel = channel,
                    Phase = failure.Phase,
                    Attempt = attempt,
                    ChangedAt = timeProvider.GetUtcNow(),
                    Trigger = trigger,
                    Failure = failure.ToPublicFailure(),
                    NextAction = nextAction,
                },
                Failure = failure,
            }
        );
    }

    private void Publish(EventSubChannelDiagnosticReport report)
    {
        try
        {
            var state = report.Status;
            lock (_gate)
            {
                _states[state.Channel] = state;
                switch (report)
                {
                    case EventSubChannelDiagnosticReport.Healthy:
                        _failures.Remove(state.Channel);
                        break;
                    case EventSubChannelDiagnosticReport.Recovering recovering:
                        _failures[state.Channel] = recovering.Failure;
                        break;
                    case EventSubChannelDiagnosticReport.Degraded degraded:
                        _failures[state.Channel] = degraded.Failure;
                        break;
                    default:
                        throw new UnreachableException(
                            "Unknown EventSub channel diagnostic report."
                        );
                }

                statusScope.Set(state);
                UpdateRuntimeStatusLocked();
            }

            diagnostics.Report(report);
        }
        catch (Exception exception)
        {
            throw new EventSubChannelStatusPublicationException(exception);
        }
    }

    private void UpdateRuntimeStatusLocked()
    {
        var healthyChannels = _states
            .Values.OfType<EventSubChannelStatus.Healthy>()
            .Select(state => state.Channel)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        BotRuntimeStatus status =
            healthyChannels.Length > 0 ? new BotRuntimeStatus.Connected(healthyChannels)
            : _authorizedChannels.Count > 0 ? new BotRuntimeStatus.Authorized()
            : new BotRuntimeStatus.Unauthorized();
        runtimeStatus.SetEventSubStatus(statusScope.Id, status);
    }

    private sealed class EventSubChannelAttemptContext
    {
        internal EventSubChannelPhase Phase { get; set; } = EventSubChannelPhase.AccountResolution;
    }
}
