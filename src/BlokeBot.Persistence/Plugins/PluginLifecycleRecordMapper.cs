using BlokeBot.Persistence.Models;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Runtime;

namespace BlokeBot.Persistence.Plugins;

internal static class PluginLifecycleRecordMapper
{
    internal static PluginLifecycleState ToDomain(PluginLifecycleRecord record)
    {
        var pluginId = PluginId.TryCreate(record.PluginId, out var parsedPluginId)
            ? parsedPluginId
            : throw new InvalidOperationException("Persisted plugin ID is invalid.");
        var selected = Installation(pluginId, record.SelectedVersion, record.SelectedTag);
        var operationId = Operation(record.OperationId);
        var selectedGeneration = Generation(record.SelectedGeneration);
        var active = ActiveRuntime(record, pluginId);
        var detail =
            record.OutcomeDetail is null ? null
            : PluginLifecycleSafeDetail.TryCreate(record.OutcomeDetail, out var parsedDetail)
                ? parsedDetail
            : throw new InvalidOperationException("Persisted lifecycle detail is invalid.");
        var state = new PluginLifecycleState(
            pluginId,
            selected,
            operationId,
            selectedGeneration,
            active,
            record.Phase,
            record.OperationKind,
            record.FaultedFrom,
            record.AutomaticRestartConsumed,
            Utc(record.RestartNotBeforeUtc),
            new(record.OutcomeCode, record.FailureCode, detail, Utc(record.OutcomeOccurredAtUtc)),
            record.Revision,
            Utc(record.UpdatedAtUtc)
        );
        return PluginLifecycleStateMachine.HasValidFaultInvariant(state)
            ? state
            : throw new InvalidOperationException("Persisted lifecycle fault state is invalid.");
    }

    internal static PluginLifecycleRecord FromDomain(PluginLifecycleState state)
    {
        var record = new PluginLifecycleRecord { PluginId = state.PluginId.Value };
        Apply(record, state);
        return record;
    }

    internal static void Apply(PluginLifecycleRecord record, PluginLifecycleState state)
    {
        if (!PluginLifecycleStateMachine.HasValidFaultInvariant(state))
        {
            throw new InvalidOperationException("Lifecycle fault state is invalid.");
        }

        record.SelectedVersion = state.SelectedInstallation.Release.DeclaredVersion.Value;
        record.SelectedTag = state.SelectedInstallation.Release.Tag.Value;
        record.OperationId = state.OperationId.Value;
        record.SelectedGeneration = checked((long)state.SelectedGeneration.Value);
        record.ActiveVersion = state.ActiveRuntime?.Installation.Release.DeclaredVersion.Value;
        record.ActiveTag = state.ActiveRuntime?.Installation.Release.Tag.Value;
        record.ActiveOperationId = state.ActiveRuntime?.Fence.OperationId.Value;
        record.ActiveGeneration = state.ActiveRuntime is null
            ? null
            : checked((long)state.ActiveRuntime.Fence.Generation.Value);
        record.Phase = state.Phase;
        record.OperationKind = state.OperationKind;
        record.FaultedFrom = state.FaultedFrom;
        record.AutomaticRestartConsumed = state.AutomaticRestartConsumed;
        record.RestartNotBeforeUtc = state.RestartNotBeforeUtc?.UtcDateTime;
        record.OutcomeCode = state.LatestOutcome.Code;
        record.FailureCode = state.LatestOutcome.FailureCode;
        record.OutcomeDetail = state.LatestOutcome.Detail?.Value;
        record.OutcomeOccurredAtUtc = state.LatestOutcome.OccurredAtUtc.UtcDateTime;
        record.Revision = state.Revision;
        record.UpdatedAtUtc = state.UpdatedAtUtc.UtcDateTime;
    }

    private static PluginLifecycleActiveRuntime? ActiveRuntime(
        PluginLifecycleRecord record,
        PluginId pluginId
    ) =>
        record.ActiveVersion is null
        && record.ActiveTag is null
        && record.ActiveOperationId is null
        && record.ActiveGeneration is null
            ? null
        : record.ActiveVersion is null
        || record.ActiveTag is null
        || record.ActiveOperationId is null
        || record.ActiveGeneration is null
            ? throw new InvalidOperationException("Persisted active plugin fence is incomplete.")
        : new(
            Installation(pluginId, record.ActiveVersion, record.ActiveTag),
            new(
                Operation(record.ActiveOperationId.Value),
                Generation(record.ActiveGeneration.Value)
            )
        );

    private static PluginInstallationIdentity Installation(
        PluginId pluginId,
        string version,
        string tag
    ) =>
        SemanticVersion.TryCreate(version, out var parsedVersion)
        && PluginGitTag.TryCreate(tag, out var parsedTag)
            ? new(pluginId, new(parsedVersion, parsedTag))
            : throw new InvalidOperationException("Persisted plugin release identity is invalid.");

    private static PluginLifecycleOperationId Operation(Guid value) =>
        PluginLifecycleOperationId.TryCreate(value, out var operationId)
            ? operationId
            : throw new InvalidOperationException("Persisted lifecycle operation ID is invalid.");

    private static PluginWorkerGeneration Generation(long value) =>
        value > 0 && PluginWorkerGeneration.TryCreate((ulong)value, out var generation)
            ? generation
            : throw new InvalidOperationException("Persisted plugin generation is invalid.");

    private static DateTimeOffset Utc(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static DateTimeOffset? Utc(DateTime? value) => value is null ? null : Utc(value.Value);
}
