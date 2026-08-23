using BlokeBot.Plugins.Contracts;

namespace BlokeBot.Plugins.Runtime;

public enum PluginLifecycleTransitionFailureCode
{
    Busy,
    AlreadyActive,
    FaultedInstallation,
    NotFound,
    NotFaulted,
    InvalidTransition,
    GenerationExhausted,
}

public abstract record PluginLifecycleTransitionOutcome
{
    private PluginLifecycleTransitionOutcome() { }

    public sealed record Applied(PluginLifecycleState State) : PluginLifecycleTransitionOutcome;

    public sealed record Rejected(PluginLifecycleTransitionFailureCode Code)
        : PluginLifecycleTransitionOutcome;
}

public static partial class PluginLifecycleStateMachine
{
    public static PluginLifecycleTransitionOutcome BeginActivation(
        PluginLifecycleState? current,
        PluginInstallationIdentity installation,
        PluginLifecycleOperationId operationId,
        DateTimeOffset now
    )
    {
        if (current is not null && IsBusy(current.Phase))
        {
            return Rejected(PluginLifecycleTransitionFailureCode.Busy);
        }

        if (
            current is { Phase: PluginLifecyclePhase.Active, ActiveRuntime: { } active }
            && active.Installation == installation
        )
        {
            return Rejected(PluginLifecycleTransitionFailureCode.AlreadyActive);
        }

        if (
            current is { Phase: PluginLifecyclePhase.Faulted }
            && current.SelectedInstallation == installation
        )
        {
            return Rejected(PluginLifecycleTransitionFailureCode.FaultedInstallation);
        }

        var releaseChanged = current is null || current.SelectedInstallation != installation;
        var selectedGeneration = current?.SelectedGeneration;
        if (
            releaseChanged
            && !PluginLifecycleGenerations.TryNext(selectedGeneration, out selectedGeneration)
        )
        {
            return Rejected(PluginLifecycleTransitionFailureCode.GenerationExhausted);
        }

        selectedGeneration ??= GenerationOne();
        return new PluginLifecycleTransitionOutcome.Applied(
            new(
                installation.PluginId,
                installation,
                operationId,
                selectedGeneration,
                current is { Phase: PluginLifecyclePhase.Active } ? current.ActiveRuntime : null,
                PluginLifecyclePhase.Preparing,
                PluginLifecycleOperationKind.Activate,
                null,
                AutomaticRestartConsumed: false,
                RestartNotBeforeUtc: null,
                PluginLifecycleOutcome.Progress(PluginLifecycleOutcomeCode.Preparing, now),
                (current?.Revision ?? -1) + 1,
                now
            )
        );
    }

    public static PluginLifecycleTransitionOutcome PreparationSucceeded(
        PluginLifecycleState state,
        DateTimeOffset now
    ) =>
        state.Phase == PluginLifecyclePhase.Preparing
            ? Applied(
                state with
                {
                    Phase = PluginLifecyclePhase.Migrating,
                    ActiveRuntime = null,
                    LatestOutcome = PluginLifecycleOutcome.Progress(
                        PluginLifecycleOutcomeCode.Migrating,
                        now
                    ),
                    Revision = state.Revision + 1,
                    UpdatedAtUtc = now,
                }
            )
            : Rejected(PluginLifecycleTransitionFailureCode.InvalidTransition);

    public static PluginLifecycleTransitionOutcome PreparationFailed(
        PluginLifecycleState state,
        PluginLifecycleFailureCode failureCode,
        PluginLifecycleSafeDetail? detail,
        DateTimeOffset now
    )
    {
        if (state.Phase != PluginLifecyclePhase.Preparing)
        {
            return Rejected(PluginLifecycleTransitionFailureCode.InvalidTransition);
        }

        var outcome = PluginLifecycleOutcome.Failure(failureCode, detail, now);
        return state.ActiveRuntime is { } active
            ? Applied(
                state with
                {
                    SelectedInstallation = active.Installation,
                    OperationId = active.Fence.OperationId,
                    SelectedGeneration = active.Fence.Generation,
                    Phase = PluginLifecyclePhase.Active,
                    LatestOutcome = outcome,
                    Revision = state.Revision + 1,
                    UpdatedAtUtc = now,
                }
            )
            : Fault(state, PluginLifecyclePhase.Preparing, outcome, now);
    }

    public static PluginLifecycleTransitionOutcome MigrationSucceeded(
        PluginLifecycleState state,
        DateTimeOffset now
    ) => Move(state, PluginLifecyclePhase.Migrating, PluginLifecyclePhase.Activating, now);

    public static PluginLifecycleTransitionOutcome ActivationSucceeded(
        PluginLifecycleState state,
        DateTimeOffset now,
        bool recovered = false
    ) =>
        state.Phase == PluginLifecyclePhase.Activating
            ? Applied(
                state with
                {
                    Phase = PluginLifecyclePhase.Active,
                    ActiveRuntime = new(state.SelectedInstallation, state.SelectedFence),
                    RestartNotBeforeUtc = null,
                    LatestOutcome = PluginLifecycleOutcome.Progress(
                        state.OperationKind == PluginLifecycleOperationKind.Restart
                                ? PluginLifecycleOutcomeCode.Restarted
                            : recovered ? PluginLifecycleOutcomeCode.Recovered
                            : PluginLifecycleOutcomeCode.Activated,
                        now
                    ),
                    Revision = state.Revision + 1,
                    UpdatedAtUtc = now,
                }
            )
            : Rejected(PluginLifecycleTransitionFailureCode.InvalidTransition);

    public static PluginLifecycleTransitionOutcome ActiveRecoverySucceeded(
        PluginLifecycleState state,
        DateTimeOffset now
    ) =>
        state.Phase == PluginLifecyclePhase.Active
            ? Applied(
                state with
                {
                    LatestOutcome = PluginLifecycleOutcome.Progress(
                        PluginLifecycleOutcomeCode.Recovered,
                        now
                    ),
                    Revision = state.Revision + 1,
                    UpdatedAtUtc = now,
                }
            )
            : Rejected(PluginLifecycleTransitionFailureCode.InvalidTransition);

    private static PluginLifecycleTransitionOutcome Move(
        PluginLifecycleState state,
        PluginLifecyclePhase expected,
        PluginLifecyclePhase next,
        DateTimeOffset now
    ) =>
        state.Phase == expected
            ? Applied(
                state with
                {
                    Phase = next,
                    Revision = state.Revision + 1,
                    UpdatedAtUtc = now,
                }
            )
            : Rejected(PluginLifecycleTransitionFailureCode.InvalidTransition);

    private static PluginLifecycleTransitionOutcome Terminal(
        PluginLifecycleState state,
        PluginLifecyclePhase expected,
        PluginLifecyclePhase next,
        PluginLifecycleOutcomeCode outcome,
        DateTimeOffset now
    ) =>
        state.Phase == expected
            ? Applied(
                state with
                {
                    Phase = next,
                    ActiveRuntime = null,
                    LatestOutcome = PluginLifecycleOutcome.Progress(outcome, now),
                    Revision = state.Revision + 1,
                    UpdatedAtUtc = now,
                }
            )
            : Rejected(PluginLifecycleTransitionFailureCode.InvalidTransition);

    private static bool IsBusy(PluginLifecyclePhase phase) =>
        phase
            is PluginLifecyclePhase.Preparing
                or PluginLifecyclePhase.Migrating
                or PluginLifecyclePhase.Activating
                or PluginLifecyclePhase.Draining
                or PluginLifecyclePhase.Removing
                or PluginLifecyclePhase.Purging;

    private static PluginLifecycleTransitionOutcome.Applied Applied(PluginLifecycleState state) =>
        new(state);

    private static PluginLifecycleTransitionOutcome.Rejected Rejected(
        PluginLifecycleTransitionFailureCode code
    ) => new(code);

    private static PluginWorkerGeneration GenerationOne() =>
        PluginWorkerGeneration.TryCreate(1, out var generation)
            ? generation
            : throw new InvalidOperationException("Generation one is invalid.");
}
