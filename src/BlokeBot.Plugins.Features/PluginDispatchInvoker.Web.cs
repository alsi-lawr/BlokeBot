using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Runtime;

namespace BlokeBot.Plugins.Features;

public abstract record PluginWebDispatchOutcome
{
    private PluginWebDispatchOutcome() { }

    public sealed record Returned(PluginValue Value) : PluginWebDispatchOutcome;

    public sealed record AuthenticationRejected : PluginWebDispatchOutcome;

    public sealed record Failed(PluginWorkerFailure Failure) : PluginWebDispatchOutcome;

    public sealed record Cancelled(PluginCancellationReason Reason) : PluginWebDispatchOutcome;

    public sealed record Rejected(PluginDispatchInvocationRejectionCode Code)
        : PluginWebDispatchOutcome;

    public sealed record Stale : PluginWebDispatchOutcome;
}

public sealed partial class PluginDispatchInvoker
{
    public ValueTask<PluginWebDispatchOutcome> InvokeWebhookAsync(
        PluginDispatchEndpoint.Webhook endpoint,
        PluginInvocationContext.Channel context,
        PluginValue input,
        CancellationToken cancellationToken
    ) => InvokeWebhookCoreAsync(endpoint, context, input, cancellationToken);

    public ValueTask<PluginWebDispatchOutcome> InvokeActionAsync(
        PluginDispatchEndpoint.Action endpoint,
        PluginInvocationContext.Channel context,
        PluginValue input,
        CancellationToken cancellationToken
    ) => InvokeWebCoreAsync(endpoint, context, input, authentication: null, cancellationToken);

    private async ValueTask<PluginWebDispatchOutcome> InvokeWebhookCoreAsync(
        PluginDispatchEndpoint.Webhook endpoint,
        PluginInvocationContext.Channel context,
        PluginValue input,
        CancellationToken cancellationToken
    )
    {
        var authentication = endpoint.Descriptor.Authentication switch
        {
            PluginWebhookAuthentication.Public => null,
            PluginWebhookAuthentication.Callback callback => callback,
            _ => throw new InvalidOperationException("Unknown plugin webhook authentication."),
        };
        return await InvokeWebCoreAsync(
            endpoint,
            context,
            input,
            authentication,
            cancellationToken
        );
    }

    private async ValueTask<PluginWebDispatchOutcome> InvokeWebCoreAsync(
        PluginDispatchEndpoint endpoint,
        PluginInvocationContext.Channel context,
        PluginValue input,
        PluginWebhookAuthentication.Callback? authentication,
        CancellationToken cancellationToken
    )
    {
        if (
            context.Plugin != endpoint.Declaration.Installation
            || context.Host != endpoint.State.Key.HostId
        )
        {
            return new PluginWebDispatchOutcome.Rejected(
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
            return new PluginWebDispatchOutcome.Rejected(
                PluginDispatchInvocationRejectionCode.FeatureUnavailable
            );
        }

        await using var admission = admitted.Admission;
        if (
            work.Admit(endpoint.State, cancellationToken)
            is not PluginDispatchWorkAdmission.Admitted workAdmitted
        )
        {
            return new PluginWebDispatchOutcome.Rejected(
                PluginDispatchInvocationRejectionCode.FeatureStopping
            );
        }

        await using var workLease = workAdmitted.Lease;
        if (authentication is not null)
        {
            var authenticationResult = await InvokeWebWorkerAsync(
                endpoint,
                context,
                authentication.Module,
                authentication.Operation,
                input,
                workLease.CancellationToken
            );
            if (!admission.ValidateWorkerResult())
            {
                return new PluginWebDispatchOutcome.Stale();
            }
            if (
                authenticationResult.Outcome
                is not PluginWorkerInvocationOutcome.Returned
                {
                    Value: PluginValue.Boolean { Value: true },
                }
            )
            {
                return
                    authenticationResult.Outcome
                        is PluginWorkerInvocationOutcome.Cancelled cancelled
                    ? new PluginWebDispatchOutcome.Cancelled(cancelled.Reason)
                    : new PluginWebDispatchOutcome.AuthenticationRejected();
            }
        }

        var result = await InvokeWebWorkerAsync(
            endpoint,
            context,
            endpoint.Module,
            endpoint.Operation,
            input,
            workLease.CancellationToken
        );
        return !admission.ValidateWorkerResult()
            ? new PluginWebDispatchOutcome.Stale()
            : result.Outcome switch
            {
                PluginWorkerInvocationOutcome.Returned returned =>
                    new PluginWebDispatchOutcome.Returned(returned.Value),
                PluginWorkerInvocationOutcome.Failed failed => new PluginWebDispatchOutcome.Failed(
                    failed.Failure
                ),
                PluginWorkerInvocationOutcome.Cancelled cancelled =>
                    new PluginWebDispatchOutcome.Cancelled(cancelled.Reason),
                _ => throw new InvalidOperationException(
                    "Unknown plugin worker invocation outcome."
                ),
            };
    }

    private ValueTask<PluginWorkerInvocationResult> InvokeWebWorkerAsync(
        PluginDispatchEndpoint endpoint,
        PluginInvocationContext.Channel context,
        PluginLuaModuleId module,
        PluginHostOperationId operation,
        PluginValue input,
        CancellationToken cancellationToken
    ) =>
        runtime.InvokeAsync(
            endpoint.State.Key.PluginId,
            endpoint.State.Fence,
            Identity(endpoint, context),
            new PluginLiveInvocation.HostAction(module, operation, input),
            cancellationToken
        );
}
