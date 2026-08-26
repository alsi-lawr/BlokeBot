using BlokeBot.Persistence.Models;
using BlokeBot.Plugins.Features;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Persistence.Plugins;

public sealed partial class EfPluginFeatureStore
{
    private async Task<PluginFeatureEnableConflictCode?> ApplyAutomationsAsync(
        BlokeBotDbContext db,
        PluginFeatureEnableStoreRequest request,
        CancellationToken cancellationToken
    )
    {
        if (request.Automation is not { } plan)
        {
            return null;
        }

        var key = request.NextState.Key;
        if (
            plan.OperationId == Guid.Empty
            || plan.PluginId != key.PluginId.Value
            || plan.FeatureId != key.FeatureId.Value
            || plan.Templates.Any(template =>
                template.Provenance.PluginId != plan.PluginId
                || template.Provenance.PluginVersion != plan.PluginVersion
                || template.Provenance.MutableTag != plan.MutableTag
                || template.Provenance.ManifestVersion != plan.ManifestVersion
                || template.Provenance.FeatureId != plan.FeatureId
            )
        )
        {
            return PluginFeatureEnableConflictCode.AutomationProvenance;
        }

        var prior = await db
            .PluginAutomationInstantiations.Include(static value => value.Flow)
            .Where(value =>
                value.PluginId == plan.PluginId
                && value.FeatureId == plan.FeatureId
                && value.HostId == key.HostId.Value
            )
            .ToArrayAsync(cancellationToken);
        var currentTemplates = plan.Templates.ToDictionary(
            static template => template.Provenance.TemplateId,
            StringComparer.Ordinal
        );
        var incompatible = prior
            .Where(value =>
                value.Flow is not null
                && (
                    !currentTemplates.TryGetValue(value.TemplateId, out var current)
                    || !Compatible(value, current.Provenance)
                )
            )
            .ToArray();
        if (incompatible.Length > 0)
        {
            foreach (var record in incompatible)
            {
                record.Flow!.IsEnabled = false;
                record.Flow.UnavailableReason =
                    $"Plugin {plan.PluginId} changed. Enable a compatible version or recreate this flow.";
                record.Flow.UpdatedAtUtc = Now();
            }
            AddRejected(db, plan, key.HostId.Value, "plugin-provenance-incompatible", prior);
            return PluginFeatureEnableConflictCode.AutomationProvenance;
        }

        foreach (var template in plan.Templates)
        {
            var existingOperation = prior.SingleOrDefault(value =>
                value.EnableOperationId == plan.OperationId
                && value.TemplateId == template.Provenance.TemplateId
            );
            if (
                existingOperation is not null
                && !Compatible(existingOperation, template.Provenance)
            )
            {
                return PluginFeatureEnableConflictCode.AutomationProvenance;
            }

            var compatible = prior
                .Where(value =>
                    Compatible(value, template.Provenance)
                    && value.Status
                        is PluginAutomationInstantiationStatus.Completed
                            or PluginAutomationInstantiationStatus.InProgress
                    && value.Flow is not null
                )
                .OrderByDescending(static value => value.UpdatedAtUtc)
                .FirstOrDefault();
            var operation = existingOperation ?? NewRecord(plan, template, key.HostId.Value);
            if (existingOperation is null)
            {
                _ = db.PluginAutomationInstantiations.Add(operation);
            }
            operation.Status = PluginAutomationInstantiationStatus.InProgress;
            operation.Diagnostic = null;
            operation.UpdatedAtUtc = Now();
            _ = await db.SaveChangesAsync(cancellationToken);

            if (compatible is null)
            {
                var nameConflict = await db.AutomationFlows.AnyAsync(
                    value => value.HostId == key.HostId.Value && value.Name == template.Name,
                    cancellationToken
                );
                if (nameConflict)
                {
                    operation.Status = PluginAutomationInstantiationStatus.Rejected;
                    operation.Diagnostic = "flow-name-conflict";
                    operation.UpdatedAtUtc = Now();
                    return PluginFeatureEnableConflictCode.AutomationName;
                }
            }

            var flow = compatible?.Flow ?? CreateFlow(key.HostId.Value, template);
            if (compatible is null)
            {
                _ = db.AutomationFlows.Add(flow);
            }
            operation.Flow = flow;
            operation.FlowId = flow.Id;
            operation.Status = PluginAutomationInstantiationStatus.Completed;
            operation.UpdatedAtUtc = Now();

            if (compatible is { Status: PluginAutomationInstantiationStatus.InProgress })
            {
                compatible.Status = PluginAutomationInstantiationStatus.Completed;
                compatible.UpdatedAtUtc = Now();
            }
        }

        return null;
    }

    private static AutomationFlow CreateFlow(int hostId, PluginAutomationTemplateStorePlan template)
    {
        var flowId = Guid.NewGuid();
        var now = Now();
        return new()
        {
            Id = flowId,
            HostId = hostId,
            Name = template.Name,
            SchemaVersion = 1,
            IsEnabled = true,
            UseVerticalLayout = false,
            UseSmoothEdges = false,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            Nodes =
            [
                .. template.Nodes.Select(node => new AutomationFlowNode
                {
                    Id = node.Id,
                    FlowId = flowId,
                    DefinitionId = node.DefinitionId,
                    DefinitionSchemaVersion = node.DefinitionSchemaVersion,
                    ConfigurationJson = node.ConfigurationJson,
                    InputBindingsJson = node.InputBindingsJson,
                    ExpressionLanguageVersion = 1,
                    ContinueOnFailure = node.ContinueOnFailure,
                    CanvasX = node.CanvasX,
                    CanvasY = node.CanvasY,
                    PluginProvenanceJson = node.ProvenanceJson,
                }),
            ],
            Edges =
            [
                .. template.Edges.Select(edge => new AutomationFlowEdge
                {
                    Id = edge.Id,
                    FlowId = flowId,
                    Kind =
                        edge.Kind == PluginAutomationStoreEdgeKind.Flow
                            ? PersistedAutomationEdgeKind.Flow
                            : PersistedAutomationEdgeKind.Data,
                    SourceNodeId = edge.SourceNodeId,
                    SourcePortId = edge.SourcePortId,
                    TargetNodeId = edge.TargetNodeId,
                    TargetPortId = edge.TargetPortId,
                }),
            ],
        };
    }

    private static PluginAutomationInstantiationRecord NewRecord(
        PluginAutomationEnableStorePlan plan,
        PluginAutomationTemplateStorePlan template,
        int hostId
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            EnableOperationId = plan.OperationId,
            PluginId = plan.PluginId,
            FeatureId = plan.FeatureId,
            HostId = hostId,
            TemplateId = template.Provenance.TemplateId,
            PluginVersion = plan.PluginVersion,
            MutableTag = plan.MutableTag,
            ManifestVersion = plan.ManifestVersion,
            TemplateHash = template.Provenance.TemplateHash,
            Status = PluginAutomationInstantiationStatus.InProgress,
            CreatedAtUtc = Now(),
            UpdatedAtUtc = Now(),
        };

    private static bool Compatible(
        PluginAutomationInstantiationRecord record,
        PluginAutomationStoreProvenance provenance
    ) =>
        record.PluginId == provenance.PluginId
        && record.PluginVersion == provenance.PluginVersion
        && record.MutableTag == provenance.MutableTag
        && record.ManifestVersion == provenance.ManifestVersion
        && record.FeatureId == provenance.FeatureId
        && record.TemplateId == provenance.TemplateId
        && record.TemplateHash == provenance.TemplateHash;

    private static void AddRejected(
        BlokeBotDbContext db,
        PluginAutomationEnableStorePlan plan,
        int hostId,
        string diagnostic,
        IReadOnlyCollection<PluginAutomationInstantiationRecord> prior,
        PluginAutomationTemplateStorePlan? template = null
    )
    {
        var templates = template is null ? plan.Templates : [template];
        foreach (var item in templates)
        {
            var record = prior.SingleOrDefault(candidate =>
                candidate.EnableOperationId == plan.OperationId
                && candidate.TemplateId == item.Provenance.TemplateId
            );
            if (record is null)
            {
                record = NewRecord(plan, item, hostId);
                _ = db.PluginAutomationInstantiations.Add(record);
            }
            record.Status = PluginAutomationInstantiationStatus.Rejected;
            record.Diagnostic = diagnostic;
            record.UpdatedAtUtc = Now();
        }
    }

    private static DateTime Now() => DateTime.UtcNow;
}
