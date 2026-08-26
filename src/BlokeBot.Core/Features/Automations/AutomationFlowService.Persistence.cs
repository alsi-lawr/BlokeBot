using System.Collections.Immutable;
using System.Text.Json;
using BlokeBot.Core.Features.HostedChannels;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.Automations;

public sealed partial class AutomationFlowService
{
    internal static AutomationFlowNode Persist(Guid flowId, AutomationFlowDraftNode node) =>
        new()
        {
            Id = node.Id.Value,
            FlowId = flowId,
            DefinitionId = node.Definition.TypeId,
            DefinitionSchemaVersion = node.Definition.SchemaVersion,
            ConfigurationJson = node.Definition.Configuration.GetRawText(),
            InputBindingsJson = AutomationRuntimeSerialization.SerializeInputBindings(
                node.InputBindings
            ),
            ExpressionLanguageVersion = node.ExpressionLanguageVersion.Value,
            ContinueOnFailure = node.FailurePolicy == AutomationNodeFailurePolicy.Continue,
            CanvasX = node.Position.X.Value,
            CanvasY = node.Position.Y.Value,
            DisplayAlias = string.IsNullOrWhiteSpace(node.DisplayAlias) ? null : node.DisplayAlias,
            PluginProvenanceJson = node.Definition.PluginProvenance is { } provenance
                ? PluginAutomationCatalogRegistry.SerializeProvenance(provenance)
                : null,
        };

    internal static AutomationFlowEdge Persist(Guid flowId, AutomationFlowDraftEdge edge) =>
        new()
        {
            Id = edge.Id,
            FlowId = flowId,
            Kind = Persist(edge.Kind),
            SourceNodeId = edge.SourceNodeId.Value,
            SourcePortId = edge.SourcePortId.Value,
            TargetNodeId = edge.TargetNodeId.Value,
            TargetPortId = edge.TargetPortId.Value,
        };

    internal static AutomationFlowDraftRestoreOutcome RestoreDraft(
        AutomationFlow flow,
        bool? enabled = null
    )
    {
        var nodes = ImmutableArray.CreateBuilder<AutomationFlowDraftNode>();
        foreach (var node in flow.Nodes)
        {
            JsonElement configuration;
            try
            {
                configuration = JsonDocument.Parse(node.ConfigurationJson).RootElement.Clone();
            }
            catch (JsonException)
            {
                return new AutomationFlowDraftRestoreOutcome.Invalid();
            }

            if (
                AutomationRuntimeSerialization.RestoreInputBindings(node.InputBindingsJson)
                is not AutomationInputBindingsRestoreOutcome.Available bindings
            )
            {
                return new AutomationFlowDraftRestoreOutcome.Invalid();
            }

            if (
                node.PluginProvenanceJson is not null
                && !PluginAutomationCatalogRegistry.TryDeserializeProvenance(
                    node.PluginProvenanceJson,
                    out _
                )
            )
            {
                return new AutomationFlowDraftRestoreOutcome.Invalid();
            }

            nodes.Add(
                new(
                    new(node.Id),
                    new(
                        node.DefinitionId,
                        node.DefinitionSchemaVersion,
                        configuration,
                        PluginAutomationCatalogRegistry.TryDeserializeProvenance(
                            node.PluginProvenanceJson,
                            out var provenance
                        )
                            ? provenance
                            : null
                    ),
                    new(node.ExpressionLanguageVersion),
                    node.ContinueOnFailure
                        ? AutomationNodeFailurePolicy.Continue
                        : AutomationNodeFailurePolicy.Stop,
                    bindings.Bindings,
                    new(new(node.CanvasX), new(node.CanvasY)),
                    node.DisplayAlias
                )
            );
        }

        var edges = flow
            .Edges.Select(static edge => new AutomationFlowDraftEdge(
                edge.Id,
                Restore(edge.Kind),
                new(edge.SourceNodeId),
                new(edge.SourcePortId),
                new(edge.TargetNodeId),
                new(edge.TargetPortId)
            ))
            .ToImmutableArray();
        return edges.Any(static edge => !Enum.IsDefined(edge.Kind))
            ? new AutomationFlowDraftRestoreOutcome.Invalid()
            : new AutomationFlowDraftRestoreOutcome.Available(
                new(
                    new(flow.Id),
                    new(flow.HostId),
                    flow.Name,
                    flow.SchemaVersion,
                    enabled ?? flow.IsEnabled,
                    nodes.ToImmutable(),
                    edges,
                    new(
                        flow.UseVerticalLayout
                            ? AutomationFlowOrientation.Vertical
                            : AutomationFlowOrientation.Horizontal,
                        flow.UseSmoothEdges
                            ? AutomationEdgeStyle.Smooth
                            : AutomationEdgeStyle.Angular
                    )
                )
            );
    }

    private static AutomationFlowDraftRestoreOutcome RestoreFrozenDraft(
        AutomationRuntimeSerialization.PersistedFlow flow
    )
    {
        var nodes = ImmutableArray.CreateBuilder<AutomationFlowDraftNode>();
        foreach (var node in flow.Nodes)
        {
            if (
                AutomationRuntimeSerialization.RestoreInputBindings(node.InputBindingsJson)
                is not AutomationInputBindingsRestoreOutcome.Available bindings
            )
            {
                return new AutomationFlowDraftRestoreOutcome.Invalid();
            }

            nodes.Add(
                new(
                    new(node.Id),
                    AutomationRuntimeSerialization.Definition(node),
                    new(node.ExpressionLanguageVersion),
                    node.ContinueOnFailure
                        ? AutomationNodeFailurePolicy.Continue
                        : AutomationNodeFailurePolicy.Stop,
                    bindings.Bindings
                )
            );
        }

        return new AutomationFlowDraftRestoreOutcome.Available(
            new(
                new(flow.Id),
                new(flow.HostId),
                "Frozen automation",
                flow.SchemaVersion,
                true,
                nodes.ToImmutable(),
                flow.Edges.Select(static edge => new AutomationFlowDraftEdge(
                        edge.Id,
                        edge.Kind,
                        new(edge.SourceNodeId),
                        new(edge.SourcePortId),
                        new(edge.TargetNodeId),
                        new(edge.TargetPortId)
                    ))
                    .ToImmutableArray(),
                new(AutomationFlowOrientation.Horizontal, AutomationEdgeStyle.Angular)
            )
        );
    }

    private static PersistedAutomationEdgeKind Persist(AutomationEdgeKind kind) =>
        kind switch
        {
            AutomationEdgeKind.Flow => PersistedAutomationEdgeKind.Flow,
            AutomationEdgeKind.Data => PersistedAutomationEdgeKind.Data,
            _ => (PersistedAutomationEdgeKind)(-1),
        };

    private static AutomationEdgeKind Restore(PersistedAutomationEdgeKind kind) =>
        kind switch
        {
            PersistedAutomationEdgeKind.Flow => AutomationEdgeKind.Flow,
            PersistedAutomationEdgeKind.Data => AutomationEdgeKind.Data,
            _ => (AutomationEdgeKind)(-1),
        };

    private static AutomationGraphError MalformedGraphError() =>
        new(null, "graph-data-invalid", "Repair or remove this malformed automation flow.");

    private static string DuplicateName(string name)
    {
        const string Prefix = "Copy of ";
        var maximumOriginalLength = 200 - Prefix.Length;
        return Prefix + name[..Math.Min(name.Length, maximumOriginalLength)];
    }

    private static ImmutableArray<AutomationGraphError> CapabilityUnavailableErrors(
        IEnumerable<(AutomationNodeId NodeId, string DefinitionId)> nodes,
        HostFeatureFlags enabled
    ) =>
        [
            .. nodes
                .Select(node =>
                    (
                        node.NodeId,
                        Unavailable: AutomationRequiredFeatures.ForDefinitions([node.DefinitionId])
                            & ~enabled
                    )
                )
                .Where(static node => node.Unavailable != HostFeatureFlags.None)
                .Select(static node => CapabilityUnavailableError(node.NodeId, node.Unavailable)),
        ];

    private static AutomationGraphError CapabilityUnavailableError(
        AutomationNodeId nodeId,
        HostFeatureFlags unavailable
    )
    {
        var names = HostFeatureCatalog
            .Cards(unavailable)
            .Where(static card => card.Enabled)
            .Select(static card => card.Name)
            .ToArray();
        return new(
            nodeId,
            "capability-unavailable",
            names.Length == 0
                ? "Turn on the required channel tool in Channel setup."
                : $"Turn on {string.Join(", ", names)} in Channel setup."
        );
    }
}
