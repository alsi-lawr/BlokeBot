using BlokeBot.Persistence.Models;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Runtime;

namespace BlokeBot.Persistence.Plugins;

internal static class PluginLifecycleOutcomeRecordMapper
{
    internal static PluginLifecycleTombstone ToDomain(PluginLifecycleOutcomeRecord record)
    {
        var pluginId = PluginId.TryCreate(record.PluginId, out var parsedPluginId)
            ? parsedPluginId
            : throw new InvalidOperationException("Persisted plugin ID is invalid.");
        var detail =
            record.OutcomeDetail is null ? null
            : PluginLifecycleSafeDetail.TryCreate(record.OutcomeDetail, out var parsedDetail)
                ? parsedDetail
            : throw new InvalidOperationException("Persisted lifecycle detail is invalid.");
        return new(
            pluginId,
            new(
                record.OutcomeCode,
                record.FailureCode,
                detail,
                new(DateTime.SpecifyKind(record.OutcomeOccurredAtUtc, DateTimeKind.Utc))
            )
        );
    }

    internal static PluginLifecycleOutcomeRecord FromDomain(PluginLifecycleTombstone tombstone) =>
        new()
        {
            PluginId = tombstone.PluginId.Value,
            OutcomeCode = tombstone.LatestOutcome.Code,
            FailureCode = tombstone.LatestOutcome.FailureCode,
            OutcomeDetail = tombstone.LatestOutcome.Detail?.Value,
            OutcomeOccurredAtUtc = tombstone.LatestOutcome.OccurredAtUtc.UtcDateTime,
        };
}
