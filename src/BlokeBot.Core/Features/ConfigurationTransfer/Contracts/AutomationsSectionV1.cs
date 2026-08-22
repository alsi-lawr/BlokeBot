using System.Text.Json;
using System.Text.Json.Serialization;
using BlokeBot.Core.Features.Automations;

namespace BlokeBot.Core.Features.ConfigurationTransfer.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AutomationsSectionV1(
    [property: JsonRequired] IReadOnlyList<AutomationFlowV1> Flows,
    [property: JsonRequired] IReadOnlyList<AutomationHostReferenceV1> HostReferences
);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AutomationFlowV1(
    [property: JsonRequired] string Id,
    [property: JsonRequired] string Name,
    [property: JsonRequired] bool Enabled,
    [property: JsonRequired] int SchemaVersion,
    [property: JsonRequired] AutomationFlowOrientation Orientation,
    [property: JsonRequired] AutomationEdgeStyle EdgeStyle,
    [property: JsonRequired] IReadOnlyList<AutomationNodeV1> Nodes,
    [property: JsonRequired] IReadOnlyList<AutomationEdgeV1> Edges
);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AutomationNodeV1(
    [property: JsonRequired] string Id,
    [property: JsonRequired] string DefinitionId,
    [property: JsonRequired] int DefinitionSchemaVersion,
    [property: JsonRequired] JsonElement Configuration,
    [property: JsonRequired] int ExpressionLanguageVersion,
    [property: JsonRequired] AutomationNodeFailurePolicy FailurePolicy,
    [property: JsonRequired] IReadOnlyList<AutomationInputBindingV1> InputBindings,
    [property: JsonRequired] int CanvasX,
    [property: JsonRequired] int CanvasY,
    string? DisplayAlias = null
);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AutomationInputBindingV1(
    [property: JsonRequired] string FieldId,
    [property: JsonRequired] AutomationInputBindingMode Mode,
    int? ExpressionLanguageVersion = null,
    string? Expression = null
);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AutomationEdgeV1(
    [property: JsonRequired] string Id,
    [property: JsonRequired] AutomationEdgeKind Kind,
    [property: JsonRequired] string SourceNodeId,
    [property: JsonRequired] string SourcePortId,
    [property: JsonRequired] string TargetNodeId,
    [property: JsonRequired] string TargetPortId
);

public enum AutomationHostReferenceKindV1
{
    CustomCommand,
    OverlayTarget,
    OverlayCue,
    CustomReward,
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AutomationHostReferenceV1(
    [property: JsonRequired] string Id,
    [property: JsonRequired] AutomationHostReferenceKindV1 Kind,
    [property: JsonRequired] string Name,
    string? ParentId = null
);
