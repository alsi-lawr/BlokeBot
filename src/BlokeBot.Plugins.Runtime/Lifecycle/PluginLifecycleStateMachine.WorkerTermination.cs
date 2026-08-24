using BlokeBot.Plugins.Contracts;

namespace BlokeBot.Plugins.Runtime;

internal abstract record PluginWorkerTerminationTransitionOutcome
{
    private PluginWorkerTerminationTransitionOutcome() { }

    internal abstract TResult Match<TResult>(
        Func<PluginLifecycleState, TResult> replacementScheduled,
        Func<PluginLifecycleState, TResult> faultShutdownScheduled,
        Func<PluginLifecycleTransitionFailureCode, TResult> rejected
    );

    internal sealed record ReplacementScheduled(PluginLifecycleState State)
        : PluginWorkerTerminationTransitionOutcome
    {
        internal override TResult Match<TResult>(
            Func<PluginLifecycleState, TResult> replacementScheduled,
            Func<PluginLifecycleState, TResult> faultShutdownScheduled,
            Func<PluginLifecycleTransitionFailureCode, TResult> rejected
        ) => replacementScheduled(State);
    }

    internal sealed record FaultShutdownScheduled(PluginLifecycleState State)
        : PluginWorkerTerminationTransitionOutcome
    {
        internal override TResult Match<TResult>(
            Func<PluginLifecycleState, TResult> replacementScheduled,
            Func<PluginLifecycleState, TResult> faultShutdownScheduled,
            Func<PluginLifecycleTransitionFailureCode, TResult> rejected
        ) => faultShutdownScheduled(State);
    }

    internal sealed record Rejected(PluginLifecycleTransitionFailureCode Code)
        : PluginWorkerTerminationTransitionOutcome
    {
        internal override TResult Match<TResult>(
            Func<PluginLifecycleState, TResult> replacementScheduled,
            Func<PluginLifecycleState, TResult> faultShutdownScheduled,
            Func<PluginLifecycleTransitionFailureCode, TResult> rejected
        ) => rejected(Code);
    }
}

public static partial class PluginLifecycleStateMachine
{
    internal static PluginWorkerTerminationTransitionOutcome ApplyWorkerTermination(
        PluginLifecycleState state,
        PluginWorkerFailure failure,
        DateTimeOffset restartNotBeforeUtc,
        DateTimeOffset now
    )
    {
        var active = ActiveRuntimeState(state);
        if (active is null)
        {
            return new PluginWorkerTerminationTransitionOutcome.Rejected(
                PluginLifecycleTransitionFailureCode.InvalidTransition
            );
        }

        if (
            failure.Code != PluginWorkerFailureCode.WorkerTerminated
            && active.AutomaticRestartConsumed
        )
        {
            return FaultShutdown(
                active,
                PluginLifecycleFailureCode.WorkerExited,
                "The admitted plugin worker exited again.",
                now
            );
        }

        var scheduled = ScheduleWorkerReplacement(
            active,
            restartNotBeforeUtc,
            now,
            consumeRestart: failure.Code != PluginWorkerFailureCode.WorkerTerminated
        );
        return scheduled is PluginLifecycleTransitionOutcome.Applied applied
                ? new PluginWorkerTerminationTransitionOutcome.ReplacementScheduled(applied.State)
            : ((PluginLifecycleTransitionOutcome.Rejected)scheduled).Code
            == PluginLifecycleTransitionFailureCode.GenerationExhausted
                ? FaultShutdown(
                    active,
                    PluginLifecycleFailureCode.GenerationExhausted,
                    "The plugin activation generation is exhausted.",
                    now
                )
            : new PluginWorkerTerminationTransitionOutcome.Rejected(
                ((PluginLifecycleTransitionOutcome.Rejected)scheduled).Code
            );
    }

    private static PluginLifecycleState? ActiveRuntimeState(PluginLifecycleState state) =>
        state switch
        {
            { Phase: PluginLifecyclePhase.Active, ActiveRuntime: not null } => state,
            { Phase: PluginLifecyclePhase.Preparing, ActiveRuntime: { } active } => state with
            {
                SelectedInstallation = active.Installation,
                OperationId = active.Fence.OperationId,
                SelectedGeneration = active.Fence.Generation,
                Phase = PluginLifecyclePhase.Active,
                RestartNotBeforeUtc = null,
            },
            _ => null,
        };

    private static PluginWorkerTerminationTransitionOutcome FaultShutdown(
        PluginLifecycleState active,
        PluginLifecycleFailureCode code,
        string safeDetail,
        DateTimeOffset now
    )
    {
        _ = PluginLifecycleSafeDetail.TryCreate(safeDetail, out var detail);
        var transition = BeginFaultShutdown(active, code, detail, now);
        return transition is PluginLifecycleTransitionOutcome.Applied applied
            ? new PluginWorkerTerminationTransitionOutcome.FaultShutdownScheduled(applied.State)
            : new PluginWorkerTerminationTransitionOutcome.Rejected(
                ((PluginLifecycleTransitionOutcome.Rejected)transition).Code
            );
    }

    private static PluginLifecycleTransitionOutcome ScheduleWorkerReplacement(
        PluginLifecycleState state,
        DateTimeOffset restartNotBeforeUtc,
        DateTimeOffset now,
        bool consumeRestart
    ) =>
        state.Phase != PluginLifecyclePhase.Active
        || (consumeRestart && state.AutomaticRestartConsumed)
            ? Rejected(PluginLifecycleTransitionFailureCode.InvalidTransition)
            : ScheduleFreshWorkerReplacement(state, restartNotBeforeUtc, now, consumeRestart);

    private static PluginLifecycleTransitionOutcome ScheduleFreshWorkerReplacement(
        PluginLifecycleState state,
        DateTimeOffset restartNotBeforeUtc,
        DateTimeOffset now,
        bool consumeRestart
    ) =>
        PluginLifecycleGenerations.TryNext(state.SelectedGeneration, out var generation)
            ? Applied(
                state with
                {
                    SelectedGeneration = generation,
                    Phase = PluginLifecyclePhase.Activating,
                    OperationKind = PluginLifecycleOperationKind.Restart,
                    AutomaticRestartConsumed = consumeRestart || state.AutomaticRestartConsumed,
                    RestartNotBeforeUtc = restartNotBeforeUtc,
                    LatestOutcome = PluginLifecycleOutcome.Progress(
                        PluginLifecycleOutcomeCode.RestartScheduled,
                        now
                    ),
                    Revision = state.Revision + 1,
                    UpdatedAtUtc = now,
                }
            )
            : Rejected(PluginLifecycleTransitionFailureCode.GenerationExhausted);
}
