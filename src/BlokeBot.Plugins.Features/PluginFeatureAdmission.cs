using BlokeBot.Plugins.Runtime;

namespace BlokeBot.Plugins.Features;

public enum PluginFeatureAdmissionRejectionCode
{
    Missing,
    Disabled,
    NotReady,
    StaleFeatureGeneration,
    StaleReadiness,
    StaleLifecycleFence,
    RuntimeRejected,
}

public enum PluginFeatureReadinessDependency
{
    Independent,
    Required,
}

public sealed record PluginFeatureAdmissionRejection(
    PluginFeatureAdmissionRejectionCode Code,
    PluginAdmissionRejectionCode? RuntimeCode = null
);

public abstract record PluginFeatureAdmissionOutcome
{
    private PluginFeatureAdmissionOutcome() { }

    public sealed record Admitted(PluginFeatureAdmission Admission) : PluginFeatureAdmissionOutcome;

    public sealed record Rejected(PluginFeatureAdmissionRejection Rejection)
        : PluginFeatureAdmissionOutcome;
}

public sealed class PluginFeatureAdmission : IAsyncDisposable
{
    private readonly PluginFeatureSnapshotRegistry _features;
    private readonly IPluginRuntimeSnapshotProvider _runtime;
    private readonly PluginRuntimeAdmission _runtimeAdmission;
    private readonly PluginFeatureReadinessDependency _readinessDependency;

    internal PluginFeatureAdmission(
        PluginFeatureState state,
        PluginFeatureSnapshotRegistry features,
        IPluginRuntimeSnapshotProvider runtime,
        PluginRuntimeAdmission runtimeAdmission,
        PluginFeatureReadinessDependency readinessDependency
    )
    {
        State = state;
        _features = features;
        _runtime = runtime;
        _runtimeAdmission = runtimeAdmission;
        _readinessDependency = readinessDependency;
    }

    public PluginFeatureState State { get; }

    public bool ValidateCallbackCompletion() =>
        Current()
        && _runtime.ValidateCallbackCompletion(State.Key.PluginId, State.Fence)
            is PluginFenceOutcome.Current;

    public bool ValidateWorkerResult() =>
        Current()
        && _runtime.ValidateWorkerResult(State.Key.PluginId, State.Fence)
            is PluginFenceOutcome.Current;

    public bool ValidateCancellation() =>
        Current()
        && _runtime.ValidateCancellation(State.Key.PluginId, State.Fence)
            is PluginFenceOutcome.Current;

    public ValueTask DisposeAsync() => _runtimeAdmission.DisposeAsync();

    private bool Current() => _features.IsCurrent(State, _readinessDependency);
}

public sealed class PluginFeatureAdmissionService(
    PluginFeatureSnapshotRegistry features,
    IPluginRuntimeSnapshotProvider runtime
)
{
    internal IPluginRuntimeSnapshotProvider Runtime => runtime;

    public PluginFeatureAdmissionOutcome Admit(
        PluginFeatureKey key,
        PluginFeatureFence expected,
        PluginFeatureReadinessDependency readinessDependency
    )
    {
        if (!features.Current.States.TryGetValue(key, out var state))
        {
            return Rejected(PluginFeatureAdmissionRejectionCode.Missing);
        }
        if (state.Fence != expected.Lifecycle)
        {
            return Rejected(PluginFeatureAdmissionRejectionCode.StaleLifecycleFence);
        }
        if (state.Generation != expected.FeatureGeneration)
        {
            return Rejected(PluginFeatureAdmissionRejectionCode.StaleFeatureGeneration);
        }
        if (state.Readiness is PluginFeatureReadiness.Disabled)
        {
            return Rejected(PluginFeatureAdmissionRejectionCode.Disabled);
        }
        if (
            readinessDependency == PluginFeatureReadinessDependency.Required
            && state.Readiness is not PluginFeatureReadiness.Ready
        )
        {
            return Rejected(PluginFeatureAdmissionRejectionCode.NotReady);
        }

        var runtimeOutcome = runtime.Admit(
            key.PluginId,
            state.Fence,
            readinessDependency == PluginFeatureReadinessDependency.Required
                ? state.AdmissionReadiness
                : PluginFeatureAdmissionReadiness.Ready
        );
        if (runtimeOutcome is PluginAdmissionOutcome.Rejected runtimeRejected)
        {
            return new PluginFeatureAdmissionOutcome.Rejected(
                new(PluginFeatureAdmissionRejectionCode.RuntimeRejected, runtimeRejected.Code)
            );
        }

        var runtimeAdmission = ((PluginAdmissionOutcome.Admitted)runtimeOutcome).Admission;
        if (!features.IsCurrent(state, readinessDependency))
        {
            _ = runtimeAdmission.DisposeAsync();
            return Rejected(PluginFeatureAdmissionRejectionCode.StaleReadiness);
        }

        return new PluginFeatureAdmissionOutcome.Admitted(
            new(state, features, runtime, runtimeAdmission, readinessDependency)
        );
    }

    private static PluginFeatureAdmissionOutcome.Rejected Rejected(
        PluginFeatureAdmissionRejectionCode code
    ) => new(new(code));
}
