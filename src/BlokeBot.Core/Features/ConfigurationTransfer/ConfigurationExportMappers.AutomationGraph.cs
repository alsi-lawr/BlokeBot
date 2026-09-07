using System.Text.Json;
using BlokeBot.Core.Features.Automations;
using BlokeBot.Core.Features.ConfigurationTransfer.Contracts;

namespace BlokeBot.Core.Features.ConfigurationTransfer;

internal static partial class ConfigurationExportMappers
{
    private static AutomationFlowV2 ExportGraph(
        AutomationFlowDraft draft,
        string id,
        IReadOnlyDictionary<AutomationSubflowRevisionId, string> revisions,
        ConfigurationExportReferencePlan references,
        IDictionary<string, AutomationHostReferenceV2> hostReferences,
        AutomationCatalogService catalog
    )
    {
        var nodeIds = draft
            .Nodes.OrderBy(node => node.Id.Value)
            .Select((node, index) => (node.Id, Label("node", index)))
            .ToDictionary(pair => pair.Id, pair => pair.Item2);
        return new(
            id,
            draft.Name,
            draft.IsEnabled,
            draft.SchemaVersion,
            draft.Canvas.Orientation,
            draft.Canvas.EdgeStyle,
            draft
                .Nodes.OrderBy(node => node.Id.Value)
                .Select(node =>
                    ExportNode(
                        draft.HostId,
                        node,
                        nodeIds[node.Id],
                        revisions,
                        references,
                        hostReferences,
                        catalog
                    )
                )
                .ToArray(),
            draft
                .Edges.OrderBy(edge => edge.Id)
                .Select(
                    (edge, index) =>
                        new AutomationEdgeV2(
                            Label("edge", index),
                            edge.Kind,
                            nodeIds[edge.SourceNodeId],
                            edge.SourcePortId.Value,
                            nodeIds[edge.TargetNodeId],
                            edge.TargetPortId.Value
                        )
                )
                .ToArray()
        );
    }

    private static AutomationNodeV2 ExportNode(
        AutomationHostId hostId,
        AutomationFlowDraftNode node,
        string id,
        IReadOnlyDictionary<AutomationSubflowRevisionId, string> revisions,
        ConfigurationExportReferencePlan references,
        IDictionary<string, AutomationHostReferenceV2> hostReferences,
        AutomationCatalogService catalog
    )
    {
        if (
            node.Definition.PluginProvenance is not null
            && (
                !catalog.TryResolvePlugin(hostId, new(node.Definition.TypeId), out var installed)
                || !AutomationPluginContractV2.Available(installed)
            )
        )
        {
            throw new AutomationConfigurationExportException(
                node.Definition.TypeId,
                "Enable the installed compatible plugin feature before exporting."
            );
        }
        if (
            catalog.ValidatePersistedDefinition(node.Definition)
            is not AutomationConfigurationCheck.Valid valid
        )
        {
            throw new AutomationConfigurationExportException(
                node.Definition.TypeId,
                "Install the compatible node contract before exporting."
            );
        }
        if (
            (
                AutomationPortableConfiguration.Rejection(valid)
                ?? AutomationPortableConfiguration.Rejection(
                    node.Definition.TypeId,
                    node.Definition.Configuration
                )
            ) is
            { } reason
        )
        {
            throw new AutomationConfigurationExportException(node.Definition.TypeId, reason);
        }
        var reference = AutomationReferenceExportMapper.Map(
            AutomationFlowService.Persist(Guid.Empty, node),
            references,
            hostReferences
        );
        if (reference.Rejection is not null)
        {
            throw new AutomationConfigurationExportException(
                node.Definition.TypeId,
                "Resolve the node's channel references before exporting."
            );
        }
        AutomationSubflowBindingV2? subflow = null;
        var configuration = reference.Configuration;
        if (AutomationSubflowDefinitions.TryRead(node.Definition, out var binding))
        {
            subflow = new(
                binding.RevisionId is { } revision ? revisions[revision] : null,
                binding.Interface,
                AutomationDataValueSerialization.SerializeOutputs(binding.FixedInputs)
            );
            configuration = JsonSerializer.SerializeToElement(new { });
        }
        return new(
            id,
            node.Definition.TypeId,
            node.Definition.SchemaVersion,
            configuration,
            node.ExpressionLanguageVersion.Value,
            node.FailurePolicy,
            node.InputBindings.OrderBy(pair => pair.Key.Value, StringComparer.Ordinal)
                .Select(pair => new AutomationInputBindingV2(
                    pair.Key.Value,
                    pair.Value.Mode,
                    pair.Value.Expression?.LanguageVersion.Value,
                    pair.Value.Expression?.Source
                ))
                .ToArray(),
            node.Position.X.Value,
            node.Position.Y.Value,
            node.DisplayAlias
        )
        {
            Plugin = node.Definition.PluginProvenance is { } plugin
                ? AutomationPluginContractV2.From(plugin)
                : null,
            Subflow = subflow,
        };
    }
}
