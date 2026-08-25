using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Features;
using BlokeBot.Plugins.Runtime;

namespace BlokeBot.Core.Features.Plugins;

public sealed class PluginHostModuleCatalog(
    IEnumerable<IPluginHostModule> modules,
    PluginFeatureAdmissionService admissions,
    ILogger<PluginHostModuleCatalog> logger
) : IPluginCoreDependencyChecker, IPluginHostCallDispatcher
{
    private readonly IReadOnlyDictionary<PluginHostModuleId, IPluginHostModule> _modules =
        modules.ToDictionary(static module => module.Descriptor.Id);

    public PluginCoreDependencyStatus Check(IReadOnlyList<PluginHostModuleRequirement> requirements)
    {
        var missing = requirements
            .Where(requirement =>
                !_modules.TryGetValue(requirement.Id, out var module)
                || module.Descriptor.Version.Value < requirement.MinimumVersion.Value
                || module.Descriptor.Version.Value > requirement.MaximumVersion.Value
            )
            .Select(static requirement => requirement.Id)
            .ToArray();
        return missing.Length == 0
            ? new PluginCoreDependencyStatus.Available()
            : new PluginCoreDependencyStatus.Missing(missing);
    }

    public ValueTask<PluginHostCallOutcome> DispatchAsync(
        PluginHostCall call,
        CancellationToken cancellationToken
    ) =>
        ValueTask.FromResult<PluginHostCallOutcome>(
            Failed(PluginHostFailureCode.Unavailable, "Host call admission is unavailable.")
        );

    public async ValueTask<PluginHostCallOutcome> DispatchAsync(
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
            || activation.WorkerGeneration != identity.Generation
            || !PluginFeatureGeneration.TryCreate(
                activation.FeatureGeneration.Value,
                out var featureGeneration
            )
            || identity.Context != call.Context
        )
        {
            return Failed(PluginHostFailureCode.ContextNotPermitted, "Host call fence is invalid.");
        }

        var key = new PluginFeatureKey(identity.Plugin.PluginId, identity.Feature, identity.Host);
        var expected = new PluginFeatureFence(
            new(operationId, activation.WorkerGeneration),
            featureGeneration
        );
        if (
            admissions.Admit(key, expected, PluginFeatureReadinessDependency.Independent)
            is not PluginFeatureAdmissionOutcome.Admitted admitted
        )
        {
            return new PluginHostCallOutcome.Cancelled(PluginCancellationReason.PluginDisabled);
        }

        await using var admission = admitted.Admission;
        if (!_modules.TryGetValue(call.Module, out var module))
        {
            return Failed(PluginHostFailureCode.NotFound, "Host module is unavailable.");
        }
        if (
            PluginHostCallValidator.ValidateCall(call, module.Descriptor)
            is not PluginHostCallValidationOutcome.Valid
        )
        {
            return Failed(PluginHostFailureCode.InvalidArguments, "Host call is invalid.");
        }

        PluginHostCallOutcome outcome;
        try
        {
            outcome = await module.InvokeAsync(identity, call, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new PluginHostCallOutcome.Cancelled(PluginCancellationReason.CallerRequested);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Plugin host module {Module} operation {Operation} failed.",
                call.Module.Value,
                call.Operation.Value
            );
            return Failed(PluginHostFailureCode.Unavailable, "Host operation failed.");
        }

        if (!admission.ValidateCallbackCompletion())
        {
            return new PluginHostCallOutcome.Cancelled(PluginCancellationReason.PluginDisabled);
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
                "Host operation returned an invalid result."
            );
    }

    private static PluginHostCallOutcome.Failed Failed(
        PluginHostFailureCode code,
        string message
    ) => new(new(code, message));
}
