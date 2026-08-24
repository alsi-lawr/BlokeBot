using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Runtime;

namespace BlokeBot.Plugins.Features;

public enum PluginReadinessReasonCode
{
    MissingScopes,
    ReconciliationPending,
    ReconciliationFailed,
}

public enum PluginRecoveryAction
{
    ReconnectTwitch,
    Retry,
}

public sealed record PluginReadinessReason
{
    private PluginReadinessReason(
        PluginReadinessReasonCode code,
        PluginRecoveryAction action,
        string detail
    )
    {
        Code = code;
        Action = action;
        Detail = detail;
    }

    public PluginReadinessReasonCode Code { get; }

    public PluginRecoveryAction Action { get; }

    public string Detail { get; }

    public static bool TryCreate(
        PluginReadinessReasonCode code,
        PluginRecoveryAction action,
        string? detail,
        out PluginReadinessReason reason
    )
    {
        var value = detail?.Trim();
        var valid =
            value is { Length: > 0 and <= PluginContractLimits.MaximumReadinessReasonCharacters };
        reason = valid ? new(code, action, value!) : null!;
        return valid;
    }
}

public abstract record PluginFeatureReadiness
{
    private PluginFeatureReadiness() { }

    public abstract TResult Match<TResult>(
        Func<Disabled, TResult> disabled,
        Func<EnabledDegraded, TResult> enabledDegraded,
        Func<Ready, TResult> ready
    );

    public sealed record Disabled : PluginFeatureReadiness
    {
        public override TResult Match<TResult>(
            Func<Disabled, TResult> disabled,
            Func<EnabledDegraded, TResult> enabledDegraded,
            Func<Ready, TResult> ready
        ) => disabled(this);
    }

    public sealed record EnabledDegraded(PluginReadinessReason Reason) : PluginFeatureReadiness
    {
        public override TResult Match<TResult>(
            Func<Disabled, TResult> disabled,
            Func<EnabledDegraded, TResult> enabledDegraded,
            Func<Ready, TResult> ready
        ) => enabledDegraded(this);
    }

    public sealed record Ready : PluginFeatureReadiness
    {
        public override TResult Match<TResult>(
            Func<Disabled, TResult> disabled,
            Func<EnabledDegraded, TResult> enabledDegraded,
            Func<Ready, TResult> ready
        ) => ready(this);
    }
}

public sealed record PluginFeatureState(
    PluginFeatureKey Key,
    PluginLifecycleFence Fence,
    PluginFeatureGeneration Generation,
    PluginFeatureReadiness Readiness,
    PluginFeatureRevision Revision
)
{
    public bool Enabled => Readiness.Match(_ => false, _ => true, _ => true);

    public PluginFeatureAdmissionReadiness AdmissionReadiness =>
        Readiness.Match(
            _ => PluginFeatureAdmissionReadiness.Disabled,
            _ => PluginFeatureAdmissionReadiness.NotReady,
            _ => PluginFeatureAdmissionReadiness.Ready
        );
}
