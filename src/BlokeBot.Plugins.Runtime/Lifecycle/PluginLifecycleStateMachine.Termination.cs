namespace BlokeBot.Plugins.Runtime;

public static partial class PluginLifecycleStateMachine
{
    public static bool HasValidFaultInvariant(PluginLifecycleState state) =>
        state.Phase == PluginLifecyclePhase.Faulted
            ? IsLegalFaultSource(state.FaultedFrom)
                && (
                    state.ActiveRuntime is not { } pending
                    || (
                        state.FaultedFrom == PluginLifecyclePhase.Active
                        && pending.Installation == state.SelectedInstallation
                        && pending.Fence == state.SelectedFence
                    )
                )
            : state.FaultedFrom is null;

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
                FaultedFrom = null,
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

    public static PluginLifecycleTransitionOutcome BeginRestart(
        PluginLifecycleState state,
        PluginLifecycleOperationId operationId,
        DateTimeOffset now
    )
    {
        if (
            state.Phase != PluginLifecyclePhase.Faulted
            || state.FaultedFrom is null
            || state.ActiveRuntime is not null
        )
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

    internal static PluginLifecycleTransitionOutcome Fault(
        PluginLifecycleState state,
        PluginLifecyclePhase failedPhase,
        PluginLifecycleFailureCode failureCode,
        PluginLifecycleSafeDetail? detail,
        DateTimeOffset now
    ) => Fault(state, failedPhase, PluginLifecycleOutcome.Failure(failureCode, detail, now), now);

    internal static PluginLifecycleTransitionOutcome BeginFaultShutdown(
        PluginLifecycleState state,
        PluginLifecycleFailureCode failureCode,
        PluginLifecycleSafeDetail? detail,
        DateTimeOffset now
    ) =>
        state is { Phase: PluginLifecyclePhase.Active, ActiveRuntime: not null }
            ? Applied(
                state with
                {
                    Phase = PluginLifecyclePhase.Faulted,
                    FaultedFrom = PluginLifecyclePhase.Active,
                    RestartNotBeforeUtc = null,
                    LatestOutcome = PluginLifecycleOutcome.Failure(failureCode, detail, now),
                    Revision = state.Revision + 1,
                    UpdatedAtUtc = now,
                }
            )
            : Rejected(PluginLifecycleTransitionFailureCode.InvalidTransition);

    internal static PluginLifecycleTransitionOutcome CompleteFaultShutdown(
        PluginLifecycleState state,
        PluginLifecycleFailureCode? shutdownFailureCode,
        PluginLifecycleSafeDetail? detail,
        DateTimeOffset now
    ) =>
        state
            is {
                Phase: PluginLifecyclePhase.Faulted,
                FaultedFrom: PluginLifecyclePhase.Active,
                ActiveRuntime: not null,
            }
            ? Applied(
                state with
                {
                    ActiveRuntime = null,
                    LatestOutcome = shutdownFailureCode is { } code
                        ? PluginLifecycleOutcome.Failure(code, detail, now)
                        : state.LatestOutcome,
                    Revision = state.Revision + 1,
                    UpdatedAtUtc = now,
                }
            )
            : Rejected(PluginLifecycleTransitionFailureCode.InvalidTransition);

    private static PluginLifecycleTransitionOutcome Fault(
        PluginLifecycleState state,
        PluginLifecyclePhase failedPhase,
        PluginLifecycleOutcome outcome,
        DateTimeOffset now
    ) =>
        state.Phase != failedPhase
        || state.Phase == PluginLifecyclePhase.Active
        || IsTerminal(state.Phase)
            ? Rejected(PluginLifecycleTransitionFailureCode.InvalidTransition)
            : Applied(
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

    private static bool IsTerminal(PluginLifecyclePhase phase) =>
        phase is PluginLifecyclePhase.Removed or PluginLifecyclePhase.Faulted;

    private static bool IsLegalFaultSource(PluginLifecyclePhase? phase) =>
        phase
            is PluginLifecyclePhase.Preparing
                or PluginLifecyclePhase.Migrating
                or PluginLifecyclePhase.Activating
                or PluginLifecyclePhase.Active
                or PluginLifecyclePhase.Draining
                or PluginLifecyclePhase.Removing
                or PluginLifecyclePhase.Purging;
}
