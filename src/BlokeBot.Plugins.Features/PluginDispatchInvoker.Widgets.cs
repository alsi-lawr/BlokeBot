using BlokeBot.Plugins.Contracts;

namespace BlokeBot.Plugins.Features;

public interface IPluginWidgetInvoker
{
    ValueTask<PluginDispatchInvocationOutcome> InvokeWidgetAsync(
        PluginWidgetEndpoint endpoint,
        PluginInvocationContext.Widget context,
        PluginValue.Map configuration,
        CancellationToken cancellationToken
    );
}

public sealed partial class PluginDispatchInvoker : IPluginWidgetInvoker
{
    public async ValueTask<PluginDispatchInvocationOutcome> InvokeWidgetAsync(
        PluginWidgetEndpoint endpoint,
        PluginInvocationContext.Widget context,
        PluginValue.Map configuration,
        CancellationToken cancellationToken
    )
    {
        if (
            context.Plugin != endpoint.Declaration.Installation
            || context.Host != endpoint.State.Key.HostId
            || context.WidgetId != endpoint.Descriptor.Id
            || context.OverlayId == Guid.Empty
            || !Enum.IsDefined(context.Mode)
            || context.DocumentId == Guid.Empty
            || context.InstanceId == Guid.Empty
            || !PluginWidgetConfiguration.IsValid(endpoint.Descriptor, configuration)
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
            is not PluginDispatchWorkAdmission.Admitted accepted
        )
        {
            return Rejected(PluginDispatchInvocationRejectionCode.FeatureStopping);
        }
        await using var lease = accepted.Lease;
        var result = await runtime.InvokeAsync(
            endpoint.State.Key.PluginId,
            endpoint.State.Fence,
            Identity(endpoint.Declaration, endpoint.State, context),
            new PluginLiveInvocation.Widget(
                endpoint.Descriptor.Module,
                endpoint.Operation,
                PluginInvocationInputSchemas.WidgetInput(configuration)
            ),
            lease.CancellationToken
        );
        return !admission.ValidateWorkerResult() ? new PluginDispatchInvocationOutcome.Stale()
            : result.Outcome is PluginWorkerInvocationOutcome.Returned returned
            && (
                returned.Value is not PluginValue.Map
                || PluginValueValidator.Validate(returned.Value)
                    is not PluginValueValidationOutcome.Valid
            )
                ? Rejected(PluginDispatchInvocationRejectionCode.InvalidContext)
            : Map(result.Outcome);
    }
}
