using BlokeBot.Plugins.Contracts;

namespace BlokeBot.Plugins.Features;

public sealed partial class PluginDispatchInvoker
{
    public ValueTask<PluginDispatchInvocationOutcome> InvokePageAsync(
        PluginPageEndpoint endpoint,
        PluginInvocationContext.Page context,
        PluginValue input,
        CancellationToken cancellationToken
    ) => InvokePageCoreAsync(endpoint, context, input, cancellationToken);

    public ValueTask<PluginDispatchInvocationOutcome> InvokePageActionAsync(
        PluginDispatchEndpoint.Action endpoint,
        PluginInvocationContext.Page context,
        PluginValue input,
        CancellationToken cancellationToken
    ) =>
        InvokeAsync(
            endpoint,
            context,
            new PluginLiveInvocation.HostAction(endpoint.Module, endpoint.Operation, input),
            cancellationToken
        );

    private async ValueTask<PluginDispatchInvocationOutcome> InvokePageCoreAsync(
        PluginPageEndpoint endpoint,
        PluginInvocationContext.Page context,
        PluginValue input,
        CancellationToken cancellationToken
    )
    {
        if (
            endpoint.Definition is not PluginPageDefinition.Generated generated
            || context.Plugin != endpoint.Definition.Declaration.Installation
            || context.Host != endpoint.State.Key.HostId
            || context.PageId != endpoint.Definition.Id
        )
        {
            return Rejected(PluginDispatchInvocationRejectionCode.InvalidContext);
        }

        var expected = new PluginFeatureFence(endpoint.State.Fence, endpoint.State.Generation);
        if (
            admissions.Admit(
                endpoint.State.Key,
                expected,
                PluginFeatureReadinessDependency.Required
            )
            is not PluginFeatureAdmissionOutcome.Admitted admitted
        )
        {
            return Rejected(PluginDispatchInvocationRejectionCode.FeatureUnavailable);
        }

        await using var admission = admitted.Admission;
        if (
            work.Admit(endpoint.State, cancellationToken)
            is not PluginDispatchWorkAdmission.Admitted workAdmitted
        )
        {
            return Rejected(PluginDispatchInvocationRejectionCode.FeatureStopping);
        }

        await using var workLease = workAdmitted.Lease;
        var result = await runtime.InvokeAsync(
            endpoint.State.Key.PluginId,
            endpoint.State.Fence,
            Identity(endpoint.Definition.Declaration, endpoint.State, context),
            new PluginLiveInvocation.Page(generated.Descriptor.Module, generated.Operation, input),
            workLease.CancellationToken
        );
        return !admission.ValidateWorkerResult()
            ? new PluginDispatchInvocationOutcome.Stale()
            : Map(result.Outcome);
    }

    private static PluginDispatchInvocationOutcome Map(PluginWorkerInvocationOutcome outcome) =>
        outcome switch
        {
            PluginWorkerInvocationOutcome.Returned returned =>
                new PluginDispatchInvocationOutcome.Returned(returned.Value),
            PluginWorkerInvocationOutcome.Failed failed =>
                new PluginDispatchInvocationOutcome.Failed(failed.Failure),
            PluginWorkerInvocationOutcome.Cancelled cancelled =>
                new PluginDispatchInvocationOutcome.Cancelled(cancelled.Reason),
            _ => throw new InvalidOperationException("Unknown plugin worker invocation outcome."),
        };

    private static PluginDispatchInvocationOutcome.Rejected Rejected(
        PluginDispatchInvocationRejectionCode code
    ) => new(code);
}
