using System.Collections.Immutable;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.Automations;

public sealed partial class AutomationRuntimeService
{
    private async Task<AdmissionPreparation> PrepareAdmissionAsync(
        BlokeBotDbContext db,
        AutomationContext context,
        AutomationFlow flow,
        AutomationFlowDraft draft,
        ImmutableArray<AutomationFlowNode> sources,
        HostFeatureFlags hostFeatures,
        CancellationToken cancellationToken
    )
    {
        var loaded = await AutomationSubflowStore.LoadClosureAsync(
            db,
            context.HostId,
            AutomationSubflowStore.Calls(draft.Nodes).Select(call => call.SubflowId),
            null,
            cancellationToken
        );
        if (loaded is not AutomationSubflowClosureOutcome.Available current)
        {
            return new AdmissionPreparation.Rejected(
                AutomationAdmissionRejectionReason.SubflowClosure
            );
        }
        var validation = await flowService.ValidatePreparedAsync(
            draft,
            current.Closure,
            AutomationFlowService.AutomationGraphAdmission.Saved,
            cancellationToken,
            db
        );
        if (
            validation.Gate is not null
            || validation.Errors.Any(error => error.Code != "capability-unavailable")
        )
        {
            return new AdmissionPreparation.Rejected(
                AutomationAdmissionRejectionReason.GraphValidation
            );
        }
        if (
            !(
                await AutomationSubflowRunReferences.ValidateClosureAsync(
                    db,
                    context.HostId,
                    current.Closure,
                    cancellationToken
                )
            ).IsEmpty
        )
        {
            return new AdmissionPreparation.Rejected(
                AutomationAdmissionRejectionReason.SubflowReferences
            );
        }
        var original = (AutomationDefinitionRestoreOutcome.Available)
            AutomationRuntimeSerialization.RestoreDefinition(
                AutomationRuntimeSerialization.SerializeDefinition(flow)
            );
        var runs = ImmutableArray.CreateBuilder<PreparedAdmissionRun>();
        foreach (var source in sources)
        {
            var runId = Guid.NewGuid();
            var frozen = AutomationFrozenSubflows.Freeze(original.Flow, runId, current.Closure);
            if (frozen is null || FreezeExecutionFences(frozen.Flow) is not { } frozenFlow)
            {
                return new AdmissionPreparation.Rejected(
                    AutomationAdmissionRejectionReason.ExecutionUnavailable
                );
            }
            var requiredFeatures = AutomationRequiredFeatures.ForDefinitions(
                frozenFlow.Nodes.Select(node => node.DefinitionId)
            );
            if (!hostFeatures.Contains(requiredFeatures))
            {
                return new AdmissionPreparation.Rejected(
                    AutomationAdmissionRejectionReason.FeatureDisabled
                );
            }
            runs.Add(new(new(runId), new(source.Id), frozenFlow, requiredFeatures));
        }
        return new AdmissionPreparation.Prepared(current.Closure, runs.ToImmutable());
    }

    private sealed record PreparedAdmissionRun(
        AutomationRunId RunId,
        AutomationNodeId SourceNodeId,
        AutomationRuntimeSerialization.PersistedFlow Flow,
        HostFeatureFlags RequiredFeatures
    );

    private abstract record AdmissionPreparation
    {
        private AdmissionPreparation() { }

        internal abstract T Match<T>(Func<Prepared, T> prepared, Func<Rejected, T> rejected);

        internal sealed record Prepared(
            AutomationSubflowClosure Closure,
            ImmutableArray<PreparedAdmissionRun> Runs
        ) : AdmissionPreparation
        {
            internal override T Match<T>(Func<Prepared, T> prepared, Func<Rejected, T> rejected) =>
                prepared(this);
        }

        internal sealed record Rejected(AutomationAdmissionRejectionReason Reason)
            : AdmissionPreparation
        {
            internal override T Match<T>(Func<Prepared, T> prepared, Func<Rejected, T> rejected) =>
                rejected(this);
        }
    }
}
