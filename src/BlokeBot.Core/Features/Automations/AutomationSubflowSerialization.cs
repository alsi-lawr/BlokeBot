using System.Collections.Immutable;
using System.Text.Json;
using BlokeBot.Persistence.Models;

namespace BlokeBot.Core.Features.Automations;

internal static class AutomationSubflowSerialization
{
    internal static string Serialize(AutomationSubflowRevision revision)
    {
        var graph = revision.Graph;
        var flow = new AutomationFlow
        {
            Id = revision.SubflowId.Value,
            HostId = graph.HostId.Value,
            SchemaVersion = graph.SchemaVersion,
            Nodes =
            [
                .. graph.Nodes.Select(node =>
                    AutomationFlowService.Persist(revision.SubflowId.Value, node)
                ),
            ],
            Edges =
            [
                .. graph.Edges.Select(edge =>
                    AutomationFlowService.Persist(revision.SubflowId.Value, edge)
                ),
            ],
        };
        return JsonSerializer.Serialize(
            new Document(
                revision.Id,
                revision.SubflowId,
                revision.Revision,
                graph.Name,
                revision.Description,
                revision.Interface,
                AutomationRuntimeSerialization.SerializeDefinition(flow),
                graph.Canvas,
                [
                    .. graph.Nodes.Select(node => new Layout(
                        node.Id,
                        node.Position,
                        node.DisplayAlias
                    )),
                ],
                revision.NodeContracts,
                revision.RequiredFeatures,
                revision.PluginDependencies,
                revision.PublishedAtUtc
            ),
            AutomationSubflowDefinitions.JsonOptions
        );
    }

    internal static AutomationSubflowRevision Restore(string json)
    {
        var document = JsonSerializer.Deserialize<Document>(
            json,
            AutomationSubflowDefinitions.JsonOptions
        )!;
        var persisted = (
            (AutomationDefinitionRestoreOutcome.Available)
                AutomationRuntimeSerialization.RestoreDefinition(document.GraphJson)
        ).Flow;
        var graph = (
            (AutomationFlowDraftRestoreOutcome.Available)
                AutomationFlowService.RestoreFrozenDraft(persisted)
        ).Draft;
        var layout = document.Layout.ToDictionary(value => value.NodeId);
        return new(
            document.Id,
            document.SubflowId,
            document.Revision,
            document.Description,
            document.Interface,
            graph with
            {
                Id = null,
                Name = document.Name,
                IsEnabled = false,
                Canvas = document.Canvas,
                Nodes =
                [
                    .. graph.Nodes.Select(node =>
                        node with
                        {
                            Position = layout[node.Id].Position,
                            DisplayAlias = layout[node.Id].Alias,
                        }
                    ),
                ],
            },
            document.NodeContracts,
            document.RequiredFeatures,
            document.PluginDependencies,
            document.PublishedAtUtc
        );
    }

    private sealed record Layout(
        AutomationNodeId NodeId,
        AutomationCanvasPosition Position,
        string? Alias
    );

    private sealed record Document(
        AutomationSubflowRevisionId Id,
        AutomationSubflowId SubflowId,
        int Revision,
        string Name,
        string Description,
        AutomationSubflowInterface Interface,
        string GraphJson,
        AutomationFlowCanvasSettings Canvas,
        ImmutableArray<Layout> Layout,
        ImmutableArray<AutomationSubflowNodeContract> NodeContracts,
        HostFeatureFlags RequiredFeatures,
        ImmutableArray<AutomationPluginProvenance> PluginDependencies,
        DateTimeOffset PublishedAtUtc
    );
}
