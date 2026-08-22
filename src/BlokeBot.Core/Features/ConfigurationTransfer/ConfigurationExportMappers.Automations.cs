using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;
using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.ConfigurationTransfer;

internal sealed record AutomationExportMapping(
    AutomationsSectionV1 Section,
    IReadOnlyList<AutomationTransferDiagnostic> Diagnostics
);

internal static partial class ConfigurationExportMappers
{
    internal static async Task<AutomationExportMapping> AutomationsAsync(
        BlokeBotDbContext db,
        int hostId,
        ConfigurationExportReferencePlan references,
        AutomationCatalogService catalog,
        AutomationFlowService flowService,
        CancellationToken cancellationToken
    )
    {
        var flows = await db
            .AutomationFlows.AsNoTracking()
            .AsSplitQuery()
            .Include(value => value.Nodes)
            .Include(value => value.Edges)
            .Where(value => value.HostId == hostId)
            .OrderBy(value => value.Name)
            .ThenBy(value => value.Id)
            .ToArrayAsync(cancellationToken);
        var hostReferences = new Dictionary<string, AutomationHostReferenceV1>(
            StringComparer.Ordinal
        );
        var diagnostics = new List<AutomationTransferDiagnostic>();
        var nodeIndex = 0;
        var edgeIndex = 0;
        var exported = new List<AutomationFlowV1>(flows.Length);
        for (var flowIndex = 0; flowIndex < flows.Length; flowIndex++)
        {
            var flow = flows[flowIndex];
            var flowLabel = AutomationTransferLabels.Flow(flowIndex);
            var unsupported = flow.Nodes.FirstOrDefault(node =>
                !catalog.IsFormat1Definition(node.DefinitionId)
            );
            if (unsupported is not null)
            {
                throw new Format1AutomationExportException(unsupported.DefinitionId);
            }
            var nodeIds = flow
                .Nodes.OrderBy(value => value.Id)
                .ToDictionary(value => value.Id, _ => AutomationTransferLabels.Node(nodeIndex++));
            var nodes = flow
                .Nodes.OrderBy(value => value.Id)
                .Select(node =>
                    Node(flowLabel, node, nodeIds[node.Id], references, hostReferences, diagnostics)
                )
                .ToArray();
            if (
                flow.Edges.Any(edge =>
                    !nodeIds.ContainsKey(edge.SourceNodeId)
                    || !nodeIds.ContainsKey(edge.TargetNodeId)
                )
            )
            {
                throw new Format1AutomationConfigurationExportException(
                    "graph",
                    "The persisted Automation graph contains an unrepresentable node reference."
                );
            }
            var edges = flow
                .Edges.OrderBy(value => value.Id)
                .Select(edge => new AutomationEdgeV1(
                    Id("edge", edgeIndex++),
                    edge.Kind switch
                    {
                        PersistedAutomationEdgeKind.Flow => AutomationEdgeKind.Flow,
                        PersistedAutomationEdgeKind.Data => AutomationEdgeKind.Data,
                        _ => throw new Format1AutomationConfigurationExportException(
                            "graph",
                            "The Automation contains an unknown edge kind."
                        ),
                    },
                    nodeIds[edge.SourceNodeId],
                    edge.SourcePortId,
                    nodeIds[edge.TargetNodeId],
                    edge.TargetPortId
                ))
                .ToArray();
            if (
                AutomationFlowService.RestoreDraft(flow)
                is not AutomationFlowDraftRestoreOutcome.Available restored
            )
            {
                throw new Format1AutomationConfigurationExportException(
                    "graph",
                    "The persisted Automation graph cannot be represented safely."
                );
            }
            var validation = await flowService.ValidateConfigurationTransferAsync(
                restored.Draft,
                cancellationToken
            );
            diagnostics.AddRange(
                validation.Errors.Select(error => new AutomationTransferDiagnostic(
                    flowLabel,
                    error.NodeId is { } nodeId
                        ? nodeIds.GetValueOrDefault(nodeId.Value, AutomationTransferLabels.NoNode)
                        : AutomationTransferLabels.NoNode,
                    error.Code,
                    AutomationTransferDiagnosticKind.Invalid
                ))
            );
            exported.Add(
                new(
                    flowLabel,
                    flow.Name,
                    flow.IsEnabled,
                    flow.SchemaVersion,
                    flow.UseVerticalLayout
                        ? AutomationFlowOrientation.Vertical
                        : AutomationFlowOrientation.Horizontal,
                    flow.UseSmoothEdges ? AutomationEdgeStyle.Smooth : AutomationEdgeStyle.Angular,
                    nodes,
                    edges
                )
            );
        }
        return new(
            new(
                exported,
                hostReferences.Values.OrderBy(value => value.Id, StringComparer.Ordinal).ToArray()
            ),
            diagnostics
        );
    }

    private static AutomationNodeV1 Node(
        string flowLabel,
        AutomationFlowNode value,
        string nodeLabel,
        ConfigurationExportReferencePlan references,
        IDictionary<string, AutomationHostReferenceV1> hostReferences,
        ICollection<AutomationTransferDiagnostic> diagnostics
    )
    {
        var reference = AutomationReferenceExportMapper.Map(value, references, hostReferences);
        var available =
            AutomationRuntimeSerialization.RestoreInputBindings(value.InputBindingsJson)
                as AutomationInputBindingsRestoreOutcome.Available
            ?? throw new Format1AutomationConfigurationExportException(
                value.DefinitionId,
                "Its persisted input bindings cannot be represented safely."
            );
        var projection = AutomationFormat1ConfigurationProjector.Project(
            value.DefinitionId,
            reference.Configuration,
            available.Bindings.ToDictionary(
                static binding => binding.Key.Value,
                static binding => binding.Value.Mode,
                StringComparer.Ordinal
            ),
            ConfigurationDocumentCodec.MaximumRecordsPerCollection
        );
        if (projection is AutomationFormat1ConfigurationProjection.Rejected rejected)
        {
            throw new Format1AutomationConfigurationExportException(
                value.DefinitionId,
                rejected.Message
            );
        }
        var projected = (AutomationFormat1ConfigurationProjection.Projected)projection;
        if (reference.PlaceholderReason is { } placeholderReason)
        {
            diagnostics.Add(
                new(
                    flowLabel,
                    nodeLabel,
                    placeholderReason,
                    AutomationTransferDiagnosticKind.Invalid
                )
            );
        }
        foreach (var reason in projected.RedactionReasons)
        {
            diagnostics.Add(
                new(flowLabel, nodeLabel, reason, AutomationTransferDiagnosticKind.Redaction)
            );
        }
        return new(
            nodeLabel,
            value.DefinitionId,
            value.DefinitionSchemaVersion,
            projected.Configuration,
            value.ExpressionLanguageVersion,
            value.ContinueOnFailure
                ? AutomationNodeFailurePolicy.Continue
                : AutomationNodeFailurePolicy.Stop,
            available
                .Bindings.OrderBy(binding => binding.Key.Value, StringComparer.Ordinal)
                .Select(binding => new AutomationInputBindingV1(
                    binding.Key.Value,
                    binding.Value.Mode,
                    binding.Value.Expression?.LanguageVersion.Value,
                    binding.Value.Expression?.Source
                ))
                .ToArray(),
            value.CanvasX,
            value.CanvasY,
            value.DisplayAlias
        );
    }

    private static string Id(string prefix, int index) => $"{prefix}-{index + 1:D4}";
}

internal sealed class Format1AutomationExportException(string definitionId) : Exception
{
    internal string DefinitionId { get; } = definitionId;
}

internal sealed class Format1AutomationConfigurationExportException(
    string definitionId,
    string reason,
    Exception? inner = null
) : Exception(reason, inner)
{
    internal string DefinitionId { get; } = definitionId;

    internal string Reason { get; } = reason;
}
