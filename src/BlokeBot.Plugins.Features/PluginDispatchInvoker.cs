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

    ValueTask<PluginWebDispatchOutcome> InvokeWebhookAsync(
        PluginDispatchEndpoint.Webhook endpoint,
        PluginInvocationContext.Channel context,
        PluginValue input,
        CancellationToken cancellationToken
    ) =>
        ValueTask.FromResult<PluginWebDispatchOutcome>(
            new PluginWebDispatchOutcome.Rejected(
                PluginDispatchInvocationRejectionCode.FeatureUnavailable
            )
        );

    ValueTask<PluginWebDispatchOutcome> InvokeActionAsync(
        PluginDispatchEndpoint.Action endpoint,
        PluginInvocationContext.Channel context,
        PluginValue input,
        CancellationToken cancellationToken
    ) =>
        ValueTask.FromResult<PluginWebDispatchOutcome>(
            new PluginWebDispatchOutcome.Rejected(
                PluginDispatchInvocationRejectionCode.FeatureUnavailable
            )
        );

    ValueTask<PluginDispatchInvocationOutcome> InvokePageAsync(
        PluginPageEndpoint endpoint,
        PluginInvocationContext.Page context,
        PluginValue input,
        CancellationToken cancellationToken
    ) =>
        ValueTask.FromResult<PluginDispatchInvocationOutcome>(
            new PluginDispatchInvocationOutcome.Rejected(
                PluginDispatchInvocationRejectionCode.FeatureUnavailable
            )
        );

    ValueTask<PluginDispatchInvocationOutcome> InvokePageActionAsync(
        PluginDispatchEndpoint.Action endpoint,
        PluginInvocationContext.Page context,
        PluginValue input,
        CancellationToken cancellationToken
    ) =>
        ValueTask.FromResult<PluginDispatchInvocationOutcome>(
            new PluginDispatchInvocationOutcome.Rejected(
                PluginDispatchInvocationRejectionCode.FeatureUnavailable
            )
        );
}

public sealed partial class PluginDispatchInvoker(
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
        PluginInvocationContext context,
        PluginLiveInvocation invocation,
        CancellationToken cancellationToken
    )
    {
        if (
            ContextPlugin(context) != endpoint.Declaration.Installation
            || ContextHost(context) != endpoint.State.Key.HostId
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
        PluginInvocationContext context
    ) => Identity(endpoint.Declaration, endpoint.State, context);

    private PluginWorkerInvocationIdentity Identity(
        PluginFeatureDeclaration declaration,
        PluginFeatureState state,
        PluginInvocationContext context
    )
    {
        _ = PluginWorkerInvocationId.TryCreate(Guid.NewGuid(), out var invocationId);
        _ = PluginCoroutineId.TryCreate(Guid.NewGuid(), out var coroutineId);
        _ = PluginWorkerCancellationId.TryCreate(Guid.NewGuid(), out var cancellationId);
        _ = PluginActivationOperationId.TryCreate(
            state.Fence.OperationId.Value,
            out var operationId
        );
        _ = PluginFeatureActivationGeneration.TryCreate(
            state.Generation.Value,
            out var featureGeneration
        );
        return new(
            declaration.Installation,
            state.Key.FeatureId,
            state.Key.HostId,
            context,
            invocationId,
            coroutineId,
            state.Fence.Generation,
            PluginWorkerDeadline.From(
                timeProvider
                    .GetUtcNow()
                    .AddMilliseconds(PluginWorkerLimits.MaximumInvocationDurationMilliseconds)
            ),
            cancellationId,
            new(operationId, state.Fence.Generation, featureGeneration)
        );
    }

    private static PluginInstallationIdentity? ContextPlugin(PluginInvocationContext context) =>
        context switch
        {
            PluginInvocationContext.Channel channel => channel.Plugin,
            PluginInvocationContext.Page page => page.Plugin,
            _ => null,
        };

    private static PluginHostId? ContextHost(PluginInvocationContext context) =>
        context switch
        {
            PluginInvocationContext.Channel channel => channel.Host,
            PluginInvocationContext.Page page => page.Host,
            _ => null,
        };
}
