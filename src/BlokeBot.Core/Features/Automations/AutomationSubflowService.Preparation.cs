using System.Collections.Immutable;
using System.Text;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Automations;

public sealed partial class AutomationSubflowService
{
    private async Task<AutomationGraphValidation> ValidateDraftAsync(
        AutomationSubflowDraft draft,
        CancellationToken cancellationToken
    )
    {
        if (
            draft.Id.Value == Guid.Empty
            || draft.Description is null
            || draft.Description.Length > 2000
            || draft.Graph.Id is not null
            || draft.Graph.IsEnabled
            || !AutomationSubflowDefinitions.ValidInterface(draft.Interface)
            || draft.Graph.Nodes.IsDefault
            || draft.Graph.Nodes.Length is < 2 or > 256
            || draft.Graph.Edges.IsDefault
            || draft.Graph.Edges.Length > 1024
        )
        {
            return ValidationFailure(
                "subflow-contract-invalid",
                "Use bounded metadata, typed ports and a source-free subflow graph."
            );
        }
        var validation = await flows.ValidateSubflowTransferAsync(draft, cancellationToken);
        return validation.Gate is not null
            ? ValidationFailure("subflow-host-unavailable", "Enable automations for this host.")
            : validation;
    }

    private async Task<Preparation> PrepareAsync(
        BlokeBotDbContext db,
        AutomationSubflowDraft draft,
        AutomationGraphValidation validation,
        CancellationToken cancellationToken
    )
    {
        var loaded = await AutomationSubflowStore.LoadClosureAsync(
            db,
            draft.Graph.HostId,
            AutomationSubflowStore.Calls(draft.Graph.Nodes).Select(pin => pin.SubflowId),
            draft.Id,
            cancellationToken
        );
        if (loaded is AutomationSubflowClosureOutcome.Invalid invalid)
        {
            return new Preparation.Invalid(invalid.Errors);
        }
        var closure = ((AutomationSubflowClosureOutcome.Available)loaded).Closure;
        var callErrors = AutomationSubflowStore.ValidateCalls(draft.Graph, closure);
        if (!callErrors.IsEmpty)
        {
            return new Preparation.Invalid(callErrors);
        }
        validation = await flows.ValidatePreparedAsync(
            draft.Graph,
            closure,
            AutomationFlowService.AutomationGraphAdmission.Saved,
            cancellationToken,
            db,
            draft.Interface,
            draft.Id
        );
        if (validation.Gate is not null || !validation.Errors.IsEmpty)
        {
            return new Preparation.Invalid(validation.Errors);
        }
        var hostId = draft.Graph.HostId.Value;
        var previousJson = await (
            from current in db.AutomationSubflows.AsNoTracking()
            join saved in db.AutomationSubflowRevisions.AsNoTracking()
                on new
                {
                    current.HostId,
                    SubflowId = current.Id,
                    Revision = current.LastRevision,
                } equals new
                {
                    saved.HostId,
                    saved.SubflowId,
                    saved.Revision,
                }
            where current.HostId == hostId && current.Id == draft.Id.Value
            select saved.SnapshotJson
        ).SingleOrDefaultAsync(cancellationToken);
        var previous = previousJson is null
            ? null
            : AutomationSubflowSerialization.Restore(previousJson);
        var number = (previous?.Revision ?? 0) + 1;
        var allNodes = draft
            .Graph.Nodes.Concat(closure.Revisions.SelectMany(revision => revision.Graph.Nodes))
            .ToArray();
        var revision = new AutomationSubflowRevision(
            new(Guid.NewGuid()),
            draft.Id,
            number,
            draft.Description.Trim(),
            draft.Interface,
            draft.Graph with
            {
                Name = draft.Graph.Name.Trim(),
            },
            validation.NodeContracts,
            AutomationRequiredFeatures.ForDefinitions(
                allNodes.Select(node => node.Definition.TypeId)
            ),
            [
                .. allNodes
                    .Select(node => node.Definition.PluginProvenance)
                    .OfType<AutomationPluginProvenance>()
                    .Distinct(),
            ],
            clock.GetUtcNow()
        );
        var enabled = await db
            .Hosts.Where(host => host.Id == hostId)
            .Select(host => host.EnabledFeatures)
            .SingleAsync(cancellationToken);
        if ((revision.RequiredFeatures & ~enabled) != HostFeatureFlags.None)
        {
            return PreparationFailure(
                "capability-unavailable",
                "Enable the subflow's required host features."
            );
        }
        var json = AutomationSubflowSerialization.Serialize(revision);
        if (Encoding.UTF8.GetByteCount(json) > 524288)
        {
            return PreparationFailure("subflow-size", "Reduce the subflow snapshot size.");
        }
        var incompatible = await IncompatibleCallersAsync(
            db,
            hostId,
            draft.Id,
            draft.Interface,
            cancellationToken
        );
        var reviewed = new AutomationSubflowReview(
            draft,
            previous?.Id,
            previous?.Interface,
            closure.Revisions.ToImmutableDictionary(child => child.SubflowId, child => child),
            revision.NodeContracts,
            revision.RequiredFeatures,
            revision.PluginDependencies,
            incompatible,
            await flows.EffectsAsync(db, draft.Graph, cancellationToken, closure.Revisions),
            await flows.EffectParentsAsync(db, draft.Graph.HostId, [revision], cancellationToken)
        );
        return new Preparation.Ready(
            revision,
            json,
            [.. incompatible.Select(impact => impact.Caller)],
            previous?.Interface,
            reviewed
        );
    }

    private static AutomationGraphValidation ValidationFailure(string code, string message) =>
        new(null, [AutomationSubflowStore.Error(code, message)]);

    private static Preparation.Invalid PreparationFailure(string code, string message) =>
        new([AutomationSubflowStore.Error(code, message)]);

    private abstract record Preparation
    {
        internal sealed record Ready(
            AutomationSubflowRevision Revision,
            string Json,
            ImmutableArray<AutomationSubflowCaller> Callers,
            AutomationSubflowInterface? PreviousInterface,
            AutomationSubflowReview Reviewed
        ) : Preparation
        {
            internal AutomationSubflowPreviewOutcome.Ready Preview =>
                new(Callers, Revision, PreviousInterface, Reviewed);
        }

        internal sealed record Invalid(ImmutableArray<AutomationGraphError> Errors) : Preparation;
    }
}
