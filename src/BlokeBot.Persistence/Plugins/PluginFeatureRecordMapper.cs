using BlokeBot.Persistence.Models;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Features;
using BlokeBot.Plugins.Runtime;

namespace BlokeBot.Persistence.Plugins;

internal static class PluginFeatureRecordMapper
{
    internal static PluginFeatureState ToDomain(PluginFeatureStateRecord record) =>
        !PluginId.TryCreate(record.PluginId, out var pluginId)
        || !PluginFeatureId.TryCreate(record.FeatureId, out var featureId)
        || !PluginHostId.TryCreate(record.HostId, out var hostId)
        || !PluginLifecycleOperationId.TryCreate(record.LifecycleOperationId, out var operationId)
        || !PluginWorkerGeneration.TryCreate(
            checked((ulong)record.WorkerGeneration),
            out var workerGeneration
        )
        || !PluginFeatureGeneration.TryCreate(
            checked((ulong)record.FeatureGeneration),
            out var featureGeneration
        )
        || !PluginFeatureRevision.TryCreate(record.Revision, out var revision)
            ? throw new InvalidOperationException("Persisted plugin feature identity is invalid.")
            : new(
                new(pluginId, featureId, hostId),
                new(operationId, workerGeneration),
                featureGeneration,
                Readiness(record),
                revision
            );

    internal static PluginFeatureStateRecord ToRecord(PluginFeatureState state)
    {
        var record = new PluginFeatureStateRecord
        {
            PluginId = state.Key.PluginId.Value,
            FeatureId = state.Key.FeatureId.Value,
            HostId = state.Key.HostId.Value,
            LifecycleOperationId = state.Fence.OperationId.Value,
            WorkerGeneration = checked((long)state.Fence.Generation.Value),
            FeatureGeneration = checked((long)state.Generation.Value),
            Revision = state.Revision.Value,
        };
        _ = state.Readiness.Match(
            _ =>
            {
                record.Readiness = PluginFeatureReadinessKind.Disabled;
                return true;
            },
            degraded =>
            {
                record.Readiness = PluginFeatureReadinessKind.EnabledDegraded;
                record.ReasonCode = degraded.Reason.Code;
                record.RecoveryAction = degraded.Reason.Action;
                record.ReasonDetail = degraded.Reason.Detail;
                return true;
            },
            _ =>
            {
                record.Readiness = PluginFeatureReadinessKind.Ready;
                return true;
            }
        );
        return record;
    }

    internal static void Apply(PluginFeatureStateRecord record, PluginFeatureState state)
    {
        var next = ToRecord(state);
        record.LifecycleOperationId = next.LifecycleOperationId;
        record.WorkerGeneration = next.WorkerGeneration;
        record.FeatureGeneration = next.FeatureGeneration;
        record.Readiness = next.Readiness;
        record.ReasonCode = next.ReasonCode;
        record.RecoveryAction = next.RecoveryAction;
        record.ReasonDetail = next.ReasonDetail;
        record.Revision = next.Revision;
    }

    private static PluginFeatureReadiness Readiness(PluginFeatureStateRecord record) =>
        record.Readiness switch
        {
            PluginFeatureReadinessKind.Disabled => new PluginFeatureReadiness.Disabled(),
            PluginFeatureReadinessKind.Ready => new PluginFeatureReadiness.Ready(),
            PluginFeatureReadinessKind.EnabledDegraded =>
                new PluginFeatureReadiness.EnabledDegraded(Reason(record)),
        };

    private static PluginReadinessReason Reason(PluginFeatureStateRecord record) =>
        record.ReasonCode is { } code
        && record.RecoveryAction is { } action
        && PluginReadinessReason.TryCreate(code, action, record.ReasonDetail, out var reason)
            ? reason
            : throw new InvalidOperationException("Persisted plugin readiness reason is invalid.");
}
