using System.Collections.Immutable;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;
using BlokeBot.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.ConfigurationTransfer;

internal sealed partial class AutomationConfigurationTransferAdapter
{
    private async Task<IReadOnlyList<MappedAutomationDraft>> BuildDraftsAsync(
        BlokeBotDbContext db,
        int hostId,
        AutomationsSectionV1 section,
        ConfigurationImportReferencePlan references,
        bool allowPlannedCommands,
        ICollection<ConfigurationValidationIssue> issues,
        CancellationToken cancellationToken
    )
    {
        var existingFlows = await db
            .AutomationFlows.AsNoTracking()
            .Where(value => value.HostId == hostId)
            .Select(value => new FlowMatch(value.Id, value.Name))
            .ToArrayAsync(cancellationToken);
        var commands = await db
            .CustomCommands.AsNoTracking()
            .Where(value => value.HostId == hostId)
            .Select(value => new CommandMatch(value.Id, value.Name))
            .ToArrayAsync(cancellationToken);
        var rewards = await db
            .TwitchCustomRewards.AsNoTracking()
            .Where(value => value.HostId == hostId)
            .Select(value => new RewardMatch(value.ProviderRewardId, value.Title))
            .ToArrayAsync(cancellationToken);
        var drafts = new List<MappedAutomationDraft>();
        var diagnosticNodeIndex = 0;
        for (var flowIndex = 0; flowIndex < section.Flows.Count; flowIndex++)
        {
            var imported = section.Flows[flowIndex];
            var diagnosticFlow = AutomationTransferLabels.Flow(flowIndex);
            var flowMatches = existingFlows
                .Where(value => SameName(value.Name, imported.Name))
                .ToArray();
            if (flowMatches.Length > 1)
            {
                issues.Add(
                    new(
                        $"sections.automations.flows[{imported.Id}]",
                        "The destination flow name is ambiguous."
                    )
                );
                continue;
            }
            var nodeIds = imported.Nodes.ToDictionary(
                value => value.Id,
                _ => new AutomationNodeId(Guid.NewGuid()),
                StringComparer.Ordinal
            );
            var diagnosticNodeLabels = imported.Nodes.ToDictionary(
                value => value.Id,
                _ => AutomationTransferLabels.Node(diagnosticNodeIndex++),
                StringComparer.Ordinal
            );
            var draftNodeLabels = nodeIds.ToDictionary(
                static pair => pair.Value,
                pair => diagnosticNodeLabels[pair.Key]
            );
            var nodes = ImmutableArray.CreateBuilder<AutomationFlowDraftNode>();
            var diagnostics = new List<AutomationTransferDiagnostic>();
            foreach (var node in imported.Nodes)
            {
                var diagnosticNode = diagnosticNodeLabels[node.Id];
                var path = $"sections.automations.flows[{imported.Id}].nodes[{node.Id}]";
                if (!catalog.IsFormat1Definition(node.DefinitionId))
                {
                    issues.Add(
                        new(path, $"Node '{node.DefinitionId}' is not a core Format 1 node.")
                    );
                    continue;
                }
                var reference = RemapConfiguration(
                    node,
                    references,
                    commands,
                    rewards,
                    allowPlannedCommands
                );
                var bindingModes = node.InputBindings.ToDictionary(
                    static binding => binding.FieldId,
                    static binding => binding.Mode,
                    StringComparer.Ordinal
                );
                var projection = AutomationFormat1ConfigurationProjector.Project(
                    node.DefinitionId,
                    reference.Configuration,
                    bindingModes,
                    ConfigurationDocumentCodec.MaximumRecordsPerCollection
                );
                if (projection is AutomationFormat1ConfigurationProjection.Rejected rejected)
                {
                    issues.Add(new(path, rejected.Message));
                    continue;
                }
                var projected = (AutomationFormat1ConfigurationProjection.Projected)projection;
                if (reference.PlaceholderReason is { } placeholderReason)
                {
                    diagnostics.Add(Invalid(diagnosticFlow, diagnosticNode, placeholderReason));
                }
                diagnostics.AddRange(
                    projected.RedactionReasons.Select(reason =>
                        Redaction(diagnosticFlow, diagnosticNode, reason)
                    )
                );
                nodes.Add(
                    new(
                        nodeIds[node.Id],
                        new(
                            node.DefinitionId,
                            node.DefinitionSchemaVersion,
                            projected.Configuration
                        ),
                        new(node.ExpressionLanguageVersion),
                        node.FailurePolicy,
                        node.InputBindings.ToImmutableDictionary(
                            value => new AutomationConfigurationFieldId(value.FieldId),
                            value => new AutomationInputBinding(
                                value.Mode,
                                value
                                    is {
                                        Expression: { } expression,
                                        ExpressionLanguageVersion: { } expressionVersion,
                                    }
                                    ? new(new(expressionVersion), expression)
                                    : null
                            )
                        ),
                        new(new(node.CanvasX), new(node.CanvasY)),
                        node.DisplayAlias
                    )
                );
            }
            var edges = imported
                .Edges.Select(edge => new AutomationFlowDraftEdge(
                    Guid.NewGuid(),
                    edge.Kind,
                    nodeIds[edge.SourceNodeId],
                    new(edge.SourcePortId),
                    nodeIds[edge.TargetNodeId],
                    new(edge.TargetPortId)
                ))
                .ToImmutableArray();
            drafts.Add(
                new(
                    imported.Id,
                    diagnosticFlow,
                    draftNodeLabels,
                    new(
                        flowMatches.Length == 1 ? new(flowMatches[0].Id) : null,
                        new(hostId),
                        imported.Name,
                        imported.SchemaVersion,
                        imported.Enabled,
                        nodes.ToImmutable(),
                        edges,
                        new(imported.Orientation, imported.EdgeStyle)
                    ),
                    diagnostics
                )
            );
        }
        return drafts;
    }

    private sealed record MappedAutomationDraft(
        string ImportedId,
        string DiagnosticFlow,
        IReadOnlyDictionary<AutomationNodeId, string> DiagnosticNodeLabels,
        AutomationFlowDraft Draft,
        IReadOnlyList<AutomationTransferDiagnostic> Diagnostics
    );

    private static AutomationTransferDiagnostic Invalid(
        string flowId,
        string nodeId,
        string reason
    ) => new(flowId, nodeId, reason, AutomationTransferDiagnosticKind.Invalid);

    private static AutomationTransferDiagnostic Redaction(
        string flowId,
        string nodeId,
        string reason
    ) => new(flowId, nodeId, reason, AutomationTransferDiagnosticKind.Redaction);

    private sealed record FlowMatch(Guid Id, string Name);
}
