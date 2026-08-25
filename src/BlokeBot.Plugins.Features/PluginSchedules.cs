using BlokeBot.Plugins.Contracts;

namespace BlokeBot.Plugins.Features;

public sealed record PluginScheduleEntry(
    Guid Id,
    PluginFeatureKey Feature,
    PluginFeatureFence Fence,
    PluginScheduleHandlerId HandlerId,
    DateTimeOffset DueAtUtc,
    long? IntervalSeconds,
    PluginValue.Map Input
);

public interface IPluginScheduleStore
{
    ValueTask<IReadOnlyList<PluginScheduleEntry>> LoadAsync(CancellationToken cancellationToken);

    ValueTask UpsertAsync(PluginScheduleEntry entry, CancellationToken cancellationToken);

    ValueTask<bool> TryConsumeOccurrenceAsync(
        PluginScheduleEntry observed,
        DateTimeOffset? nextDueAtUtc,
        CancellationToken cancellationToken
    );

    ValueTask RemoveAsync(Guid scheduleId, CancellationToken cancellationToken);

    ValueTask RemoveFeatureAsync(
        PluginFeatureKey feature,
        PluginFeatureFence fence,
        CancellationToken cancellationToken
    );

    ValueTask RemovePluginAsync(PluginId pluginId, CancellationToken cancellationToken);
}

public sealed class EmptyPluginScheduleStore : IPluginScheduleStore
{
    public ValueTask<IReadOnlyList<PluginScheduleEntry>> LoadAsync(
        CancellationToken cancellationToken
    ) => ValueTask.FromResult<IReadOnlyList<PluginScheduleEntry>>([]);

    public ValueTask UpsertAsync(PluginScheduleEntry entry, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;

    public ValueTask<bool> TryConsumeOccurrenceAsync(
        PluginScheduleEntry observed,
        DateTimeOffset? nextDueAtUtc,
        CancellationToken cancellationToken
    ) => ValueTask.FromResult(false);

    public ValueTask RemoveAsync(Guid scheduleId, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;

    public ValueTask RemoveFeatureAsync(
        PluginFeatureKey feature,
        PluginFeatureFence fence,
        CancellationToken cancellationToken
    ) => ValueTask.CompletedTask;

    public ValueTask RemovePluginAsync(PluginId pluginId, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;
}
