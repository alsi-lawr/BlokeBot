using System.Collections.Immutable;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Runtime;

namespace BlokeBot.Plugins.Features;

public sealed record PluginAutomationStoreProvenance(
    string PluginId,
    string PluginVersion,
    string MutableTag,
    int ManifestVersion,
    string FeatureId,
    string TemplateId,
    string TemplateHash
);

public sealed record PluginAutomationStoreNode(
    Guid Id,
    string DefinitionId,
    int DefinitionSchemaVersion,
    string ConfigurationJson,
    string InputBindingsJson,
    string ProvenanceJson,
    bool ContinueOnFailure,
    int CanvasX,
    int CanvasY
);

public enum PluginAutomationStoreEdgeKind
{
    Flow,
    Data,
}

public sealed record PluginAutomationStoreEdge(
    Guid Id,
    PluginAutomationStoreEdgeKind Kind,
    Guid SourceNodeId,
    string SourcePortId,
    Guid TargetNodeId,
    string TargetPortId
);

public sealed record PluginAutomationTemplateStorePlan(
    string Name,
    PluginAutomationStoreProvenance Provenance,
    ImmutableArray<PluginAutomationStoreNode> Nodes,
    ImmutableArray<PluginAutomationStoreEdge> Edges
);

public sealed record PluginAutomationEnableStorePlan(
    Guid OperationId,
    string PluginId,
    string PluginVersion,
    string MutableTag,
    int ManifestVersion,
    string FeatureId,
    ImmutableArray<PluginAutomationTemplateStorePlan> Templates
);

public abstract record PluginAutomationPlanOutcome
{
    private PluginAutomationPlanOutcome() { }

    public sealed record Prepared(PluginAutomationEnableStorePlan Plan)
        : PluginAutomationPlanOutcome;

    public sealed record Rejected(string Diagnostic) : PluginAutomationPlanOutcome;
}

public interface IPluginFeatureAutomationPlanner
{
    PluginAutomationPlanOutcome Prepare(
        PluginFeatureDeclaration declaration,
        PluginFeatureDescriptor feature,
        PluginFeatureState state,
        Guid operationId
    );
}

public interface IPluginAutomationCatalogSink
{
    void PublishDeclaration(PluginFeatureDeclaration declaration);

    void RemoveDeclaration(PluginId pluginId, PluginLifecycleFence fence);

    void PublishFeatures(PluginFeatureSnapshot snapshot);
}

public sealed record PluginAutomationEndpoint(
    PluginFeatureDeclaration Declaration,
    PluginFeatureState State,
    PluginAutomationDefinitionDescriptor Descriptor
);

public interface IPluginAutomationInvoker
{
    ValueTask<PluginDispatchInvocationOutcome> InvokeAutomationAsync(
        PluginAutomationEndpoint endpoint,
        PluginInvocationContext.Automation context,
        PluginValue input,
        CancellationToken cancellationToken
    );
}

public sealed record PluginAutomationSourceEmission(
    PluginAutomationDefinitionId DefinitionId,
    PluginValue.Map Outputs
);

public interface IPluginAutomationSourceAdmission
{
    ValueTask AdmitAsync(
        PluginDispatchEndpoint endpoint,
        PluginInvocationContext.Channel context,
        ImmutableArray<PluginAutomationSourceEmission> emissions,
        CancellationToken cancellationToken
    );
}

internal static class PluginAutomationCallbackResult
{
    private const string _sourcesProperty = "$automationSources";

    internal static ImmutableArray<PluginAutomationSourceEmission> Emissions(PluginValue value)
    {
        if (
            value is not PluginValue.Map map
            || map.Properties.SingleOrDefault(property => property.Name == _sourcesProperty)?.Value
                is not PluginValue.Array sources
        )
        {
            return [];
        }

        var emissions = ImmutableArray.CreateBuilder<PluginAutomationSourceEmission>();
        foreach (var source in sources.Items)
        {
            if (
                source is not PluginValue.Map sourceMap
                || sourceMap
                    .Properties.SingleOrDefault(property => property.Name == "definition")
                    ?.Value
                    is not PluginValue.String definition
                || !PluginAutomationDefinitionId.TryCreate(definition.Value, out var definitionId)
                || sourceMap
                    .Properties.SingleOrDefault(property => property.Name == "outputs")
                    ?.Value
                    is not PluginValue.Map outputs
            )
            {
                return [];
            }
            emissions.Add(new(definitionId, outputs));
        }

        return emissions.ToImmutable();
    }
}
