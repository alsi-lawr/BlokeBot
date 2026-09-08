using System.Text.Json;
using System.Text.Json.Serialization;
using BlokeBot.Core.Features.Automations;

namespace BlokeBot.Core.Features.ConfigurationTransfer.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AutomationsSectionV2(
    [property: JsonRequired] IReadOnlyList<AutomationFlowV2> Flows,
    [property: JsonRequired] IReadOnlyList<AutomationHostReferenceV2> HostReferences
)
{
    [JsonRequired]
    public IReadOnlyList<AutomationSubflowV2> Subflows { get; init; } = [];

    [JsonRequired]
    public IReadOnlyList<AutomationScenarioV2> Scenarios { get; init; } = [];
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AutomationFlowV2(
    [property: JsonRequired] string Id,
    [property: JsonRequired] string Name,
    [property: JsonRequired] bool Enabled,
    [property: JsonRequired] int SchemaVersion,
    [property: JsonRequired] AutomationFlowOrientation Orientation,
    [property: JsonRequired] AutomationEdgeStyle EdgeStyle,
    [property: JsonRequired] IReadOnlyList<AutomationNodeV2> Nodes,
    [property: JsonRequired] IReadOnlyList<AutomationEdgeV2> Edges
);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AutomationNodeV2(
    [property: JsonRequired] string Id,
    [property: JsonRequired] string DefinitionId,
    [property: JsonRequired] int DefinitionSchemaVersion,
    [property: JsonRequired] JsonElement Configuration,
    [property: JsonRequired] int ExpressionLanguageVersion,
    [property: JsonRequired] AutomationNodeFailurePolicy FailurePolicy,
    [property: JsonRequired] IReadOnlyList<AutomationInputBindingV2> InputBindings,
    [property: JsonRequired] int CanvasX,
    [property: JsonRequired] int CanvasY,
    string? DisplayAlias = null
)
{
    public AutomationPluginContractV2? Plugin { get; init; }
    public AutomationSubflowBindingV2? Subflow { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AutomationInputBindingV2(
    [property: JsonRequired] string FieldId,
    [property: JsonRequired] AutomationInputBindingMode Mode,
    int? ExpressionLanguageVersion = null,
    string? Expression = null
);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AutomationEdgeV2(
    [property: JsonRequired] string Id,
    [property: JsonRequired] AutomationEdgeKind Kind,
    [property: JsonRequired] string SourceNodeId,
    [property: JsonRequired] string SourcePortId,
    [property: JsonRequired] string TargetNodeId,
    [property: JsonRequired] string TargetPortId
);

public enum AutomationHostReferenceKindV2
{
    CustomCommand,
    OverlayTarget,
    OverlayCue,
    CustomReward,
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AutomationHostReferenceV2(
    [property: JsonRequired] string Id,
    [property: JsonRequired] AutomationHostReferenceKindV2 Kind,
    [property: JsonRequired] string Name,
    string? ParentId = null
);
