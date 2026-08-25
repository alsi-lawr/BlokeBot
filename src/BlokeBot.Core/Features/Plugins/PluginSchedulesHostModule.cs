using System.Globalization;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Features;
using BlokeBot.Plugins.Runtime;

namespace BlokeBot.Core.Features.Plugins;

public sealed class PluginSchedulesHostModule(
    IPluginScheduleStore store,
    IPluginDispatchSnapshotProvider dispatch,
    TimeProvider timeProvider
) : IPluginHostModule
{
    private const int _maximumSchedulesPerFeature = 128;
    private const long _maximumIntervalSeconds = 31_536_000;

    public PluginHostModuleDescriptor Descriptor => PluginStandardHostModules.Schedules;

    public ValueTask<PluginHostCallOutcome> InvokeAsync(
        PluginHostCall call,
        CancellationToken cancellationToken
    ) =>
        ValueTask.FromResult<PluginHostCallOutcome>(
            Failed(PluginHostFailureCode.Unavailable, "Schedule admission is unavailable.")
        );

    public async ValueTask<PluginHostCallOutcome> InvokeAsync(
        PluginWorkerInvocationIdentity identity,
        PluginHostCall call,
        CancellationToken cancellationToken
    )
    {
        if (
            identity.Activation is not { } activation
            || !PluginLifecycleOperationId.TryCreate(
                activation.OperationId.Value,
                out var operationId
            )
            || !PluginFeatureGeneration.TryCreate(
                activation.FeatureGeneration.Value,
                out var featureGeneration
            )
        )
        {
            return Failed(PluginHostFailureCode.InvalidArguments, "Schedule identity is invalid.");
        }
        var feature = new PluginFeatureKey(
            identity.Plugin.PluginId,
            identity.Feature,
            identity.Host
        );
        var fence = new PluginFeatureFence(
            new(operationId, activation.WorkerGeneration),
            featureGeneration
        );
        if (call.Operation == Descriptor.Operations[2].Id)
        {
            return await CancelAsync(feature, fence, call, cancellationToken);
        }
        if (
            !PluginScheduleHandlerId.TryCreate(
                ((PluginValue.String)call.Arguments[0]).Value,
                out var handlerId
            )
        )
        {
            return Failed(PluginHostFailureCode.InvalidArguments, "Schedule handler is invalid.");
        }
        if (!HandlerIsCurrent(feature, fence, handlerId))
        {
            return Failed(PluginHostFailureCode.NotFound, "Schedule handler is unavailable.");
        }
        if (
            !DateTimeOffset.TryParse(
                ((PluginValue.String)call.Arguments[1]).Value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var dueAtUtc
            )
            || dueAtUtc <= timeProvider.GetUtcNow()
            || dueAtUtc > timeProvider.GetUtcNow().AddYears(1)
        )
        {
            return Failed(PluginHostFailureCode.InvalidArguments, "Schedule due time is invalid.");
        }
        var interval =
            call.Operation == Descriptor.Operations[1].Id
                ? ((PluginValue.Number)call.Arguments[2]).Value
                : (double?)null;
        if (
            interval is { } seconds
            && (!double.IsInteger(seconds) || seconds is < 1 or > _maximumIntervalSeconds)
        )
        {
            return Failed(PluginHostFailureCode.InvalidArguments, "Schedule interval is invalid.");
        }
        var existing = await store.LoadAsync(cancellationToken);
        if (existing.Count(entry => entry.Feature == feature) >= _maximumSchedulesPerFeature)
        {
            return Failed(PluginHostFailureCode.Conflict, "Schedule limit is reached.");
        }
        var id = Guid.NewGuid();
        var inputIndex = interval.HasValue ? 3 : 2;
        await store.UpsertAsync(
            new(
                id,
                feature,
                fence,
                handlerId,
                dueAtUtc,
                interval is null ? null : checked((long)interval.Value),
                (PluginValue.Map)call.Arguments[inputIndex]
            ),
            cancellationToken
        );
        return new PluginHostCallOutcome.Returned(new PluginValue.String(id.ToString("D")));
    }

    private async ValueTask<PluginHostCallOutcome> CancelAsync(
        PluginFeatureKey feature,
        PluginFeatureFence fence,
        PluginHostCall call,
        CancellationToken cancellationToken
    )
    {
        if (!Guid.TryParse(((PluginValue.String)call.Arguments[0]).Value, out var scheduleId))
        {
            return Failed(PluginHostFailureCode.InvalidArguments, "Schedule ID is invalid.");
        }
        var entry = (await store.LoadAsync(cancellationToken)).SingleOrDefault(candidate =>
            candidate.Id == scheduleId && candidate.Feature == feature && candidate.Fence == fence
        );
        if (entry is null)
        {
            return Failed(PluginHostFailureCode.NotFound, "Schedule is unavailable.");
        }
        await store.RemoveAsync(scheduleId, cancellationToken);
        return PluginChatHostModule.Returned();
    }

    private bool HandlerIsCurrent(
        PluginFeatureKey feature,
        PluginFeatureFence fence,
        PluginScheduleHandlerId handlerId
    ) =>
        dispatch.Current.Schedules.Any(endpoint =>
            endpoint.State.Key == feature
            && endpoint.State.Fence == fence.Lifecycle
            && endpoint.State.Generation == fence.FeatureGeneration
            && endpoint.Descriptor.Id == handlerId
        );

    private static PluginHostCallOutcome.Failed Failed(
        PluginHostFailureCode code,
        string message
    ) => new(new(code, message));
}
