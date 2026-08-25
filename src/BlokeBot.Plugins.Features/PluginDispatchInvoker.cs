using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Runtime;

namespace BlokeBot.Plugins.Features;

public enum PluginDispatchInvocationRejectionCode
{
    FeatureUnavailable,
    FeatureStopping,
    InvalidContext,
}

public abstract record PluginDispatchInvocationOutcome
{
    private PluginDispatchInvocationOutcome() { }

    public sealed record Returned(PluginValue Value) : PluginDispatchInvocationOutcome;

    public sealed record Failed(PluginWorkerFailure Failure) : PluginDispatchInvocationOutcome;

    public sealed record Cancelled(PluginCancellationReason Reason)
        : PluginDispatchInvocationOutcome;

    public sealed record Rejected(PluginDispatchInvocationRejectionCode Code)
        : PluginDispatchInvocationOutcome;

    public sealed record Stale : PluginDispatchInvocationOutcome;
}

public interface IPluginDispatchInvoker
{
    ValueTask<PluginDispatchInvocationOutcome> InvokeCommandAsync(
        PluginDispatchEndpoint.Command endpoint,
        PluginInvocationContext.Channel context,
        PluginValue input,
        CancellationToken cancellationToken
    );

    ValueTask<PluginDispatchInvocationOutcome> InvokeEventAsync(
        PluginDispatchEndpoint.Event endpoint,
        PluginInvocationContext.Channel context,
        PluginValue input,
        CancellationToken cancellationToken
    );

    ValueTask<PluginDispatchInvocationOutcome> InvokeScheduleAsync(
        PluginDispatchEndpoint.Schedule endpoint,
        PluginInvocationContext.Channel context,
        PluginValue input,
        CancellationToken cancellationToken
    );
}

public sealed class PluginDispatchInvoker(
    PluginFeatureAdmissionService admissions,
    IPluginRuntimeInvoker runtime,
    PluginDispatchWorkRegistry work,
    TimeProvider timeProvider
) : IPluginDispatchInvoker
{
    public ValueTask<PluginDispatchInvocationOutcome> InvokeCommandAsync(
        PluginDispatchEndpoint.Command endpoint,
        PluginInvocationContext.Channel context,
        PluginValue input,
        CancellationToken cancellationToken
    ) =>
        InvokeAsync(
            endpoint,
            context,
            new PluginLiveInvocation.Command(endpoint.Module, endpoint.Operation, input),
            cancellationToken
        );

    public ValueTask<PluginDispatchInvocationOutcome> InvokeEventAsync(
        PluginDispatchEndpoint.Event endpoint,
        PluginInvocationContext.Channel context,
        PluginValue input,
        CancellationToken cancellationToken
    ) =>
        InvokeAsync(
            endpoint,
            context,
            new PluginLiveInvocation.Event(endpoint.Module, endpoint.Operation, input),
            cancellationToken
        );

    public ValueTask<PluginDispatchInvocationOutcome> InvokeScheduleAsync(
        PluginDispatchEndpoint.Schedule endpoint,
        PluginInvocationContext.Channel context,
        PluginValue input,
        CancellationToken cancellationToken
    ) =>
        InvokeAsync(
            endpoint,
            context,
            new PluginLiveInvocation.Schedule(endpoint.Module, endpoint.Operation, input),
            cancellationToken
        );

    private async ValueTask<PluginDispatchInvocationOutcome> InvokeAsync(
        PluginDispatchEndpoint endpoint,
        PluginInvocationContext.Channel context,
        PluginLiveInvocation invocation,
        CancellationToken cancellationToken
    )
    {
        if (
            context.Plugin != endpoint.Declaration.Installation
            || context.Host != endpoint.State.Key.HostId
        )
        {
            return new PluginDispatchInvocationOutcome.Rejected(
                PluginDispatchInvocationRejectionCode.InvalidContext
            );
        }

        var expected = new PluginFeatureFence(endpoint.State.Fence, endpoint.State.Generation);
        var readiness = endpoint.Requirements.TwitchReady
            ? PluginFeatureReadinessDependency.Required
            : PluginFeatureReadinessDependency.Independent;
        if (
            admissions.Admit(endpoint.State.Key, expected, readiness)
            is not PluginFeatureAdmissionOutcome.Admitted admitted
        )
        {
            return new PluginDispatchInvocationOutcome.Rejected(
                PluginDispatchInvocationRejectionCode.FeatureUnavailable
            );
        }

        await using var admission = admitted.Admission;
        if (
            work.Admit(endpoint.State, cancellationToken)
            is not PluginDispatchWorkAdmission.Admitted workAdmitted
        )
        {
            return new PluginDispatchInvocationOutcome.Rejected(
                PluginDispatchInvocationRejectionCode.FeatureStopping
            );
        }

        await using var workLease = workAdmitted.Lease;
        var identity = Identity(endpoint, context);
        var result = await runtime.InvokeAsync(
            endpoint.State.Key.PluginId,
            endpoint.State.Fence,
            identity,
            invocation,
            workLease.CancellationToken
        );
        return !admission.ValidateWorkerResult()
            ? new PluginDispatchInvocationOutcome.Stale()
            : result.Outcome switch
            {
                PluginWorkerInvocationOutcome.Returned returned =>
                    new PluginDispatchInvocationOutcome.Returned(returned.Value),
                PluginWorkerInvocationOutcome.Failed failed =>
                    new PluginDispatchInvocationOutcome.Failed(failed.Failure),
                PluginWorkerInvocationOutcome.Cancelled cancelled =>
                    new PluginDispatchInvocationOutcome.Cancelled(cancelled.Reason),
                _ => throw new InvalidOperationException(
                    "Unknown plugin worker invocation outcome."
                ),
            };
    }

    private PluginWorkerInvocationIdentity Identity(
        PluginDispatchEndpoint endpoint,
        PluginInvocationContext.Channel context
    )
    {
        _ = PluginWorkerInvocationId.TryCreate(Guid.NewGuid(), out var invocationId);
        _ = PluginCoroutineId.TryCreate(Guid.NewGuid(), out var coroutineId);
        _ = PluginWorkerCancellationId.TryCreate(Guid.NewGuid(), out var cancellationId);
        _ = PluginActivationOperationId.TryCreate(
            endpoint.State.Fence.OperationId.Value,
            out var operationId
        );
        _ = PluginFeatureActivationGeneration.TryCreate(
            endpoint.State.Generation.Value,
            out var featureGeneration
        );
        return new(
            endpoint.Declaration.Installation,
            endpoint.State.Key.FeatureId,
            endpoint.State.Key.HostId,
            context,
            invocationId,
            coroutineId,
            endpoint.State.Fence.Generation,
            PluginWorkerDeadline.From(
                timeProvider
                    .GetUtcNow()
                    .AddMilliseconds(PluginWorkerLimits.MaximumInvocationDurationMilliseconds)
            ),
            cancellationId,
            new(operationId, endpoint.State.Fence.Generation, featureGeneration)
        );
    }
}
