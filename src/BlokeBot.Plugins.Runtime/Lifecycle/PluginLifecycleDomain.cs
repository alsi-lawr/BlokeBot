using BlokeBot.Plugins.Contracts;

namespace BlokeBot.Plugins.Runtime;

public enum PluginLifecyclePhase
{
    Preparing,
    Migrating,
    Activating,
    Active,
    Draining,
    Removing,
    Removed,
    Faulted,
}

public enum PluginLifecycleOperationKind
{
    Activate,
    Remove,
    Restart,
}

public enum PluginLifecycleFailureCode
{
    PreparationRejected,
    PreparationFailed,
    MigrationFailed,
    ActivationFailed,
    WorkerStartFailed,
    WorkerDisposalFailed,
    WorkerExited,
    DrainTimedOut,
    CancellationFailed,
    RemovalFailed,
    RecoveryPackageUnavailable,
    RecoveryFailed,
    GenerationExhausted,
}

public enum PluginLifecycleOutcomeCode
{
    Preparing,
    Migrating,
    Activated,
    Removed,
    RestartScheduled,
    Restarted,
    Faulted,
    Recovered,
}

public sealed record PluginLifecycleSafeDetail
{
    public const int MaximumLength = 256;

    private PluginLifecycleSafeDetail(string value) => Value = value;

    public string Value { get; }

    public static bool TryCreate(string? candidate, out PluginLifecycleSafeDetail detail)
    {
        var value = candidate?.Trim();
        var valid = value is { Length: > 0 and <= MaximumLength };
        detail = valid ? new(value!) : null!;
        return valid;
    }
}

public sealed record PluginLifecycleOutcome(
    PluginLifecycleOutcomeCode Code,
    PluginLifecycleFailureCode? FailureCode,
    PluginLifecycleSafeDetail? Detail,
    DateTimeOffset OccurredAtUtc
)
{
    public static PluginLifecycleOutcome Progress(
        PluginLifecycleOutcomeCode code,
        DateTimeOffset occurredAtUtc
    ) => new(code, null, null, occurredAtUtc);

    public static PluginLifecycleOutcome Failure(
        PluginLifecycleFailureCode failureCode,
        PluginLifecycleSafeDetail? detail,
        DateTimeOffset occurredAtUtc
    ) => new(PluginLifecycleOutcomeCode.Faulted, failureCode, detail, occurredAtUtc);
}

public sealed record PluginLifecycleActiveRuntime(
    PluginInstallationIdentity Installation,
    PluginLifecycleFence Fence
);

public sealed record PluginLifecycleState(
    PluginId PluginId,
    PluginInstallationIdentity SelectedInstallation,
    PluginLifecycleOperationId OperationId,
    PluginWorkerGeneration SelectedGeneration,
    PluginLifecycleActiveRuntime? ActiveRuntime,
    PluginLifecyclePhase Phase,
    PluginLifecycleOperationKind OperationKind,
    PluginLifecyclePhase? FaultedFrom,
    bool AutomaticRestartConsumed,
    DateTimeOffset? RestartNotBeforeUtc,
    PluginLifecycleOutcome LatestOutcome,
    long Revision,
    DateTimeOffset UpdatedAtUtc
)
{
    public PluginLifecycleFence SelectedFence => new(OperationId, SelectedGeneration);
}

public sealed record PluginLifecycleView(
    PluginInstallationIdentity Installation,
    PluginLifecyclePhase Phase,
    PluginLifecycleOperationId OperationId,
    PluginWorkerGeneration Generation,
    PluginLifecycleOutcome LatestOutcome,
    bool AutomaticRestartConsumed
)
{
    public static PluginLifecycleView From(PluginLifecycleState state) =>
        new(
            state.SelectedInstallation,
            state.Phase,
            state.OperationId,
            state.SelectedGeneration,
            state.LatestOutcome,
            state.AutomaticRestartConsumed
        );
}
