using System.Collections.Immutable;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Automations;

public sealed partial class AutomationFlowService
{
    public async Task<AutomationFlowSaveOutcome> SaveAsync(
        AutomationFlowDraft draft,
        CancellationToken cancellationToken
    ) => await SaveCoreAsync(draft, null, cancellationToken);

    private async Task<AutomationFlowSaveOutcome> SaveCoreAsync(
        AutomationFlowDraft draft,
        AutomationAuthoredFlowSource? source,
        CancellationToken cancellationToken
    )
    {
        if (source is not null && !SourcePayloadMatches(draft, source))
        {
            return new AutomationFlowSaveOutcome.Invalid([
                new(
                    null,
                    "source-payload-mismatch",
                    "The source no longer matches the validated candidate. Validate it again."
                ),
            ]);
        }
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
        if (draft.Nodes.Any(node => node.Definition.TypeId == AutomationSubflowDefinitions.Invoke))
        {
            if (
                await MainDatabaseStatements.LockHostAsync(
                    db,
                    draft.HostId.Value,
                    cancellationToken
                ) == 0
            )
            {
                return new AutomationFlowSaveOutcome.HostNotFound();
            }
        }
        var loaded = await AutomationSubflowStore.LoadClosureAsync(
            db,
            draft.HostId,
            AutomationSubflowStore.Calls(draft.Nodes).Select(call => call.SubflowId),
            null,
            cancellationToken
        );
        if (loaded is AutomationSubflowClosureOutcome.Invalid invalidClosure)
        {
            return new AutomationFlowSaveOutcome.Invalid(invalidClosure.Errors);
        }
        validation = await ValidatePreparedAsync(
            draft,
            ((AutomationSubflowClosureOutcome.Available)loaded).Closure,
            AutomationGraphAdmission.Saved,
            cancellationToken,
            db
        );
        if (!validation.Errors.IsEmpty)
        {
            return new AutomationFlowSaveOutcome.Invalid(validation.Errors);
        }
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

            var bindingFieldErrors = TransformInputBindingFieldErrors(existing, draft);
            if (!bindingFieldErrors.IsEmpty)
            {
                return new AutomationFlowSaveOutcome.Invalid(bindingFieldErrors);
            }
            if (source is not null)
            {
                foreach (var node in draft.Nodes)
                {
                    var old = existing.Nodes.SingleOrDefault(value =>
                        value.Id == node.Id.Value && value.DefinitionId == node.Definition.TypeId
                    );
                    if (old is null)
                    {
                        continue;
                    }

                    var validProvenance = PluginAutomationCatalogRegistry.TryDeserializeProvenance(
                        old.PluginProvenanceJson,
                        out var provenance
                    );
                    if (
                        (old.PluginProvenanceJson is not null && !validProvenance)
                        || (provenance is null) != (node.Definition.PluginProvenance is null)
                        || (
                            provenance is not null
                            && !provenance.SameCode(node.Definition.PluginProvenance!)
                        )
                    )
                    {
                        return new AutomationFlowSaveOutcome.Invalid([
                            new(
                                node.Id,
                                "source-provider-changed",
                                "The stored node provider changed. Reopen the source before saving."
                            ),
                        ]);
                    }
                }
                draft = draft with { IsEnabled = existing.IsEnabled };
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
        var persistedNodes = draft.Nodes.Select(node => Persist(flow.Id, node)).ToArray();
        if (source is not null)
        {
            // These exact strings produced the validated draft; do not serialize a different payload.
            foreach (var node in persistedNodes)
            {
                var authored = source.Nodes.Single(value => value.Id == node.Id);
                node.ConfigurationJson = authored.ConfigurationJson;
                node.InputBindingsJson = authored.InputBindingsJson;
                node.DisplayAlias = authored.DisplayAlias;
            }
        }
        db.AutomationFlowNodes.AddRange(persistedNodes);
        db.AutomationFlowEdges.AddRange(draft.Edges.Select(edge => Persist(flow.Id, edge)));
        db.AutomationSubflowCallers.AddRange(
            AutomationSubflowStore
                .Calls(draft.Nodes)
                .Select(pin => new AutomationSubflowCallerReference
                {
                    NodeId = pin.NodeId.Value,
                    HostId = draft.HostId.Value,
                    SubflowId = pin.SubflowId.Value,
                })
        );
        _ = await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await ReconcileEventSubAsync(cancellationToken);
        return new AutomationFlowSaveOutcome.Saved(new(flow.Id));
    }

    private static ImmutableArray<AutomationGraphError> TransformInputBindingFieldErrors(
        AutomationFlow existing,
        AutomationFlowDraft candidate
    )
    {
        var candidateNodes = candidate.Nodes.ToDictionary(static node => node.Id.Value);
        var errors = ImmutableArray.CreateBuilder<AutomationGraphError>();
        foreach (
            var old in existing.Nodes.Where(node =>
                node.DefinitionId == AutomationDefinitionIds.CelTransform.Value
            )
        )
        {
            if (
                !candidateNodes.TryGetValue(old.Id, out var node)
                || node.Definition.TypeId != old.DefinitionId
            )
            {
                continue;
            }

            using var document = TryReadConfiguration(old.ConfigurationJson);
            if (
                document is null
                || !AutomationCelTransform.TryReadInputIdentities(
                    document.RootElement,
                    out var identities
                )
                || !AutomationCelTransform.TryReadInputIdentities(
                    node.Definition.Configuration,
                    out var candidateIdentities
                )
            )
            {
                errors.Add(
                    new(
                        new(old.Id),
                        "transform-input-identities-invalid",
                        "The stored Transform input identities cannot be matched safely. This repair was not saved."
                    )
                );
                continue;
            }
            foreach (var (port, field) in identities)
            {
                if (
                    candidateIdentities.TryGetValue(port, out var replacement)
                    && replacement != field
                )
                {
                    errors.Add(
                        new(
                            new(old.Id),
                            "transform-input-binding-field-changed",
                            "Create a new Transform input instead of changing its binding field.",
                            field,
                            port
                        )
                    );
                }
            }
        }
        return errors.ToImmutable();
    }

    private static System.Text.Json.JsonDocument? TryReadConfiguration(string text)
    {
        try
        {
            return System.Text.Json.JsonDocument.Parse(text);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
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
