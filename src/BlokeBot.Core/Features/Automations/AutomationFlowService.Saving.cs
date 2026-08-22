using System.Collections.Immutable;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Automations;

public sealed partial class AutomationFlowService
{
    public async Task<AutomationFlowSaveOutcome> SaveAsync(
        AutomationFlowDraft draft,
        CancellationToken cancellationToken
    )
    {
        var validation = await ValidateAsync(draft, cancellationToken);
        if (validation.Gate is { } gate)
        {
            return gate switch
            {
                AutomationCatalogAvailability.Disabled =>
                    new AutomationFlowSaveOutcome.FeatureDisabled(),
                AutomationCatalogAvailability.HostNotFound =>
                    new AutomationFlowSaveOutcome.HostNotFound(),
                _ => throw new InvalidOperationException("Unexpected automation catalog state."),
            };
        }

        if (!validation.Errors.IsEmpty)
        {
            return new AutomationFlowSaveOutcome.Invalid(validation.Errors);
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        AutomationFlow flow;
        if (draft.Id is { } existingId)
        {
            var existing = await db
                .AutomationFlows.AsNoTracking()
                .Include(static value => value.Nodes)
                .Include(static value => value.Edges)
                .SingleOrDefaultAsync(
                    value => value.Id == existingId.Value && value.HostId == draft.HostId.Value,
                    cancellationToken
                );
            if (existing is null)
            {
                return new AutomationFlowSaveOutcome.FlowNotFound();
            }

            if (RestoreDraft(existing) is not AutomationFlowDraftRestoreOutcome.Available restored)
            {
                return new AutomationFlowSaveOutcome.Invalid([MalformedGraphError()]);
            }

            var bindingFieldErrors = TransformInputBindingFieldErrors(restored.Draft, draft);
            if (!bindingFieldErrors.IsEmpty)
            {
                return new AutomationFlowSaveOutcome.Invalid(bindingFieldErrors);
            }

            flow = await db.AutomationFlows.SingleAsync(
                value => value.Id == existingId.Value && value.HostId == draft.HostId.Value,
                cancellationToken
            );
            _ = await db
                .AutomationFlowEdges.Where(value => value.FlowId == flow.Id)
                .ExecuteDeleteAsync(cancellationToken);
            _ = await db
                .AutomationFlowNodes.Where(value => value.FlowId == flow.Id)
                .ExecuteDeleteAsync(cancellationToken);
        }
        else
        {
            flow = new AutomationFlow
            {
                Id = Guid.NewGuid(),
                HostId = draft.HostId.Value,
                CreatedAtUtc = clock.GetUtcNow().UtcDateTime,
            };
            _ = db.AutomationFlows.Add(flow);
        }

        flow.Name = draft.Name.Trim();
        flow.SchemaVersion = draft.SchemaVersion;
        flow.IsEnabled = draft.IsEnabled;
        flow.UseVerticalLayout = draft.Canvas.Orientation == AutomationFlowOrientation.Vertical;
        flow.UseSmoothEdges = draft.Canvas.EdgeStyle == AutomationEdgeStyle.Smooth;
        flow.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
        db.AutomationFlowNodes.AddRange(draft.Nodes.Select(node => Persist(flow.Id, node)));
        db.AutomationFlowEdges.AddRange(draft.Edges.Select(edge => Persist(flow.Id, edge)));
        _ = await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await ReconcileEventSubAsync(cancellationToken);
        return new AutomationFlowSaveOutcome.Saved(new(flow.Id));
    }

    private ImmutableArray<AutomationGraphError> TransformInputBindingFieldErrors(
        AutomationFlowDraft existing,
        AutomationFlowDraft candidate
    )
    {
        var candidateNodes = candidate.Nodes.ToDictionary(static node => node.Id);
        var errors = ImmutableArray.CreateBuilder<AutomationGraphError>();
        foreach (var existingNode in existing.Nodes)
        {
            if (
                !candidateNodes.TryGetValue(existingNode.Id, out var candidateNode)
                || catalog.ValidatePersistedDefinition(existingNode.Definition)
                    is not AutomationConfigurationCheck.Valid
                    {
                        Configuration: AutomationCelTransformConfiguration existingTransform,
                    }
                || catalog.ValidatePersistedDefinition(candidateNode.Definition)
                    is not AutomationConfigurationCheck.Valid
                    {
                        Configuration: AutomationCelTransformConfiguration candidateTransform,
                    }
            )
            {
                continue;
            }

            var candidateInputs = candidateTransform.Inputs.ToDictionary(static input =>
                input.PortId
            );
            foreach (var existingInput in existingTransform.Inputs)
            {
                if (
                    candidateInputs.TryGetValue(existingInput.PortId, out var candidateInput)
                    && candidateInput.BindingFieldId != existingInput.BindingFieldId
                )
                {
                    errors.Add(
                        new(
                            existingNode.Id,
                            "transform-input-binding-field-changed",
                            "Create a new Transform input instead of changing its binding field.",
                            existingInput.BindingFieldId,
                            existingInput.PortId
                        )
                    );
                }
            }
        }

        return errors.ToImmutable();
    }

    public async Task<AutomationFlowEnableOutcome> SetEnabledAsync(
        AutomationHostId hostId,
        AutomationFlowId flowId,
        bool enabled,
        CancellationToken cancellationToken
    )
    {
        var availability = await catalog.DiscoverAsync(hostId, cancellationToken);
        if (availability.Availability == AutomationCatalogAvailability.Disabled)
        {
            return new AutomationFlowEnableOutcome.FeatureDisabled();
        }

        if (availability.Availability == AutomationCatalogAvailability.HostNotFound)
        {
            return new AutomationFlowEnableOutcome.HostNotFound();
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var flow = await db
            .AutomationFlows.Include(static value => value.Nodes)
            .Include(static value => value.Edges)
            .SingleOrDefaultAsync(
                value => value.Id == flowId.Value && value.HostId == hostId.Value,
                cancellationToken
            );
        if (flow is null)
        {
            return new AutomationFlowEnableOutcome.FlowNotFound();
        }

        var enabledFeatures = await db
            .Hosts.AsNoTracking()
            .Where(value => value.Id == hostId.Value)
            .Select(static value => value.EnabledFeatures)
            .SingleAsync(cancellationToken);
        var capabilityErrors = CapabilityUnavailableErrors(
            flow.Nodes.Select(static node => (new AutomationNodeId(node.Id), node.DefinitionId)),
            enabledFeatures
        );
        if (enabled && !capabilityErrors.IsEmpty)
        {
            return new AutomationFlowEnableOutcome.Invalid(capabilityErrors);
        }

        if (enabled)
        {
            if (
                RestoreDraft(flow, enabled)
                is not AutomationFlowDraftRestoreOutcome.Available restored
            )
            {
                return new AutomationFlowEnableOutcome.Invalid([MalformedGraphError()]);
            }

            var validation = await ValidateAsync(restored.Draft, cancellationToken);
            if (!validation.Errors.IsEmpty)
            {
                return new AutomationFlowEnableOutcome.Invalid(validation.Errors);
            }
        }

        flow.IsEnabled = enabled;
        flow.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
        _ = await db.SaveChangesAsync(cancellationToken);
        await ReconcileEventSubAsync(cancellationToken);
        return new AutomationFlowEnableOutcome.Updated();
    }

    private async Task ReconcileEventSubAsync(CancellationToken cancellationToken)
    {
        // Enabled-flow changes alter which EventSub subscriptions the host runtime needs.
        if (eventSub is not null)
        {
            await eventSub.ReconcileAsync(cancellationToken);
        }
    }
}
