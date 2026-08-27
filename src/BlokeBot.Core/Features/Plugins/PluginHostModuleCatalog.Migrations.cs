using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Runtime;

namespace BlokeBot.Core.Features.Plugins;

public sealed partial class PluginHostModuleCatalog
{
    private async ValueTask<PluginHostCallOutcome> DispatchMigrationAsync(
        PluginWorkerInvocationIdentity identity,
        PluginHostCall call,
        CancellationToken cancellationToken
    )
    {
        if (
            identity.Context != call.Context
            || identity.Activation is not { } activation
            || activation.WorkerGeneration != identity.Generation
            || !PluginLifecycleOperationId.TryCreate(
                activation.OperationId.Value,
                out var operationId
            )
        )
        {
            return Failed(
                PluginHostFailureCode.ContextNotPermitted,
                "Migration host-call fence is invalid."
            );
        }

        var fence = new PluginLifecycleFence(operationId, identity.Generation);
        if (!CurrentMigration(identity.Plugin, fence))
        {
            return new PluginHostCallOutcome.Cancelled(PluginCancellationReason.PluginUpdating);
        }

        if (
            !_modules.TryGetValue(call.Module, out var module)
            || (
                module.Descriptor.Id != PluginStandardHostModules.Storage.Id
                && module.Descriptor.Id != PluginStandardHostModules.Diagnostics.Id
                && module.Descriptor.Id != PluginStandardHostModules.Context.Id
            )
        )
        {
            return Failed(
                PluginHostFailureCode.ContextNotPermitted,
                "Host module is unavailable during migration."
            );
        }

        if (
            PluginHostCallValidator.ValidateCall(call, module.Descriptor)
            is not PluginHostCallValidationOutcome.Valid
        )
        {
            return Failed(
                PluginHostFailureCode.InvalidArguments,
                "Migration host call is invalid."
            );
        }

        var outcome = await module.InvokeAsync(identity, call, cancellationToken);
        if (!CurrentMigration(identity.Plugin, fence))
        {
            return new PluginHostCallOutcome.Cancelled(PluginCancellationReason.PluginUpdating);
        }

        var operation = module.Descriptor.Operations.Single(candidate =>
            candidate.Id == call.Operation
        );
        return
            PluginHostCallValidator.ValidateOutcome(outcome, operation)
            is PluginHostCallValidationOutcome.Valid
            ? outcome
            : Failed(
                PluginHostFailureCode.ProviderRejected,
                "Migration host operation returned an invalid result."
            );
    }

    private bool CurrentMigration(
        PluginInstallationIdentity installation,
        PluginLifecycleFence fence
    ) =>
        admissions.Runtime.Current.Entries.TryGetValue(installation.PluginId, out var entry)
        && entry.Installation == installation
        && entry.Fence == fence
        && entry.Phase == PluginLifecyclePhase.Migrating;
}
