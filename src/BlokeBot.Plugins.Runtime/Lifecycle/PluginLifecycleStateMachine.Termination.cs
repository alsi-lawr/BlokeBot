namespace BlokeBot.Plugins.Runtime;

public static partial class PluginLifecycleStateMachine
{
    public static PluginLifecycleTransitionOutcome BeginRemoval(
        PluginLifecycleState state,
        PluginLifecycleOperationId operationId,
        bool purge,
        DateTimeOffset now
    )
    {
        if (IsBusy(state.Phase))
        {
            return Rejected(PluginLifecycleTransitionFailureCode.Busy);
        }

        if (state.Phase == PluginLifecyclePhase.Purged)
        {
            return Rejected(PluginLifecycleTransitionFailureCode.NotFound);
        }

        var kind = purge ? PluginLifecycleOperationKind.Purge : PluginLifecycleOperationKind.Remove;
        var phase = state.ActiveRuntime is null
            ? purge
                ? PluginLifecyclePhase.Purging
                : PluginLifecyclePhase.Removing
            : PluginLifecyclePhase.Draining;
        return Applied(
            state with
            {
                OperationId = operationId,
                Phase = phase,
                OperationKind = kind,
                Revision = state.Revision + 1,
                UpdatedAtUtc = now,
            }
        );
    }

    public static PluginLifecycleTransitionOutcome DrainSucceeded(
        PluginLifecycleState state,
        DateTimeOffset now
    )
    {
        if (state.Phase != PluginLifecyclePhase.Draining)
        {
            return Rejected(PluginLifecycleTransitionFailureCode.InvalidTransition);
        }

        var phase = state.OperationKind switch
        {
            PluginLifecycleOperationKind.Remove => PluginLifecyclePhase.Removing,
            PluginLifecycleOperationKind.Purge => PluginLifecyclePhase.Purging,
            PluginLifecycleOperationKind.Restart => PluginLifecyclePhase.Activating,
            PluginLifecycleOperationKind.Activate => PluginLifecyclePhase.Migrating,
        };
        return Applied(
            state with
            {
                Phase = phase,
                ActiveRuntime = null,
                Revision = state.Revision + 1,
                UpdatedAtUtc = now,
            }
        );
    }

    public static PluginLifecycleTransitionOutcome RemovalSucceeded(
        PluginLifecycleState state,
        DateTimeOffset now
    ) =>
        Terminal(
            state,
            PluginLifecyclePhase.Removing,
            PluginLifecyclePhase.Removed,
            PluginLifecycleOutcomeCode.Removed,
            now
        );

    public static PluginLifecycleTransitionOutcome PurgeSucceeded(
        PluginLifecycleState state,
        DateTimeOffset now
    ) =>
        Terminal(
            state,
            PluginLifecyclePhase.Purging,
            PluginLifecyclePhase.Purged,
            PluginLifecycleOutcomeCode.Purged,
            now
        );

    public static PluginLifecycleTransitionOutcome BeginRestart(
        PluginLifecycleState state,
        PluginLifecycleOperationId operationId,
        DateTimeOffset now
    )
    {
        if (state.Phase != PluginLifecyclePhase.Faulted || state.FaultedFrom is null)
        {
            return Rejected(PluginLifecycleTransitionFailureCode.NotFaulted);
        }

        var phase = state.FaultedFrom.Value switch
        {
            PluginLifecyclePhase.Preparing => PluginLifecyclePhase.Preparing,
            PluginLifecyclePhase.Migrating => PluginLifecyclePhase.Migrating,
            PluginLifecyclePhase.Activating => PluginLifecyclePhase.Activating,
            PluginLifecyclePhase.Active => PluginLifecyclePhase.Activating,
            PluginLifecyclePhase.Draining => PluginLifecyclePhase.Draining,
            PluginLifecyclePhase.Removing => PluginLifecyclePhase.Removing,
            PluginLifecyclePhase.Purging => PluginLifecyclePhase.Purging,
            PluginLifecyclePhase.Removed => (PluginLifecyclePhase?)null,
            PluginLifecyclePhase.Purged => null,
            PluginLifecyclePhase.Faulted => null,
        };
        var operationKind = state.FaultedFrom.Value
            is PluginLifecyclePhase.Draining
                or PluginLifecyclePhase.Removing
                or PluginLifecyclePhase.Purging
            ? state.OperationKind
            : PluginLifecycleOperationKind.Restart;
        return phase is null
            ? Rejected(PluginLifecycleTransitionFailureCode.InvalidTransition)
            : Applied(
                state with
                {
                    OperationId = operationId,
                    Phase = phase.Value,
                    OperationKind = operationKind,
                    FaultedFrom = null,
                    AutomaticRestartConsumed = false,
                    RestartNotBeforeUtc = null,
                    Revision = state.Revision + 1,
                    UpdatedAtUtc = now,
                }
            );
    }

    public static PluginLifecycleTransitionOutcome ScheduleAutomaticRestart(
        PluginLifecycleState state,
        DateTimeOffset restartNotBeforeUtc,
        DateTimeOffset now
    ) =>
        state.Phase == PluginLifecyclePhase.Active && !state.AutomaticRestartConsumed
            ? Applied(
                state with
                {
                    Phase = PluginLifecyclePhase.Activating,
                    ActiveRuntime = null,
                    OperationKind = PluginLifecycleOperationKind.Restart,
                    AutomaticRestartConsumed = true,
                    RestartNotBeforeUtc = restartNotBeforeUtc,
                    LatestOutcome = PluginLifecycleOutcome.Progress(
                        PluginLifecycleOutcomeCode.RestartScheduled,
                        now
                    ),
                    Revision = state.Revision + 1,
                    UpdatedAtUtc = now,
                }
            )
            : Rejected(PluginLifecycleTransitionFailureCode.InvalidTransition);

    public static PluginLifecycleTransitionOutcome ScheduleExpectedRestart(
        PluginLifecycleState state,
        DateTimeOffset restartNotBeforeUtc,
        DateTimeOffset now
    ) =>
        state.Phase == PluginLifecyclePhase.Active
            ? Applied(
                state with
                {
                    Phase = PluginLifecyclePhase.Activating,
                    ActiveRuntime = null,
                    OperationKind = PluginLifecycleOperationKind.Restart,
                    RestartNotBeforeUtc = restartNotBeforeUtc,
                    LatestOutcome = PluginLifecycleOutcome.Progress(
                        PluginLifecycleOutcomeCode.RestartScheduled,
                        now
                    ),
                    Revision = state.Revision + 1,
                    UpdatedAtUtc = now,
                }
            )
            : Rejected(PluginLifecycleTransitionFailureCode.InvalidTransition);

    public static PluginLifecycleTransitionOutcome Fault(
        PluginLifecycleState state,
        PluginLifecyclePhase failedPhase,
        PluginLifecycleFailureCode failureCode,
        PluginLifecycleSafeDetail? detail,
        DateTimeOffset now
    ) => Fault(state, failedPhase, PluginLifecycleOutcome.Failure(failureCode, detail, now), now);

    private static PluginLifecycleTransitionOutcome Fault(
        PluginLifecycleState state,
        PluginLifecyclePhase failedPhase,
        PluginLifecycleOutcome outcome,
        DateTimeOffset now
    ) =>
        Applied(
            state with
            {
                Phase = PluginLifecyclePhase.Faulted,
                ActiveRuntime = null,
                FaultedFrom = failedPhase,
                RestartNotBeforeUtc = null,
                LatestOutcome = outcome,
                Revision = state.Revision + 1,
                UpdatedAtUtc = now,
            }
        );
}
