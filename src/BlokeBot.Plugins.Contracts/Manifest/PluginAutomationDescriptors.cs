using System.Collections.Immutable;

namespace BlokeBot.Plugins.Contracts;

public enum PluginAutomationDefinitionKind
{
    Source,
    Action,
    Value,
    Control,
    Transform,
}

public sealed record PluginAutomationFieldDescriptor(
    PluginAutomationFieldId Id,
    string Name,
    PluginValueKind ValueKind,
    bool Required
);

public sealed record PluginAutomationDefinitionDescriptor(
    PluginAutomationDefinitionId Id,
    PluginFeatureId FeatureId,
    PluginAutomationDefinitionKind Kind,
    string Name,
    string Description,
    PluginLuaModuleId Module,
    string EntryPoint,
    ImmutableArray<PluginAutomationFieldDescriptor> Inputs,
    ImmutableArray<PluginAutomationFieldDescriptor> Outputs
);

public sealed record PluginAutomationTemplateDescriptor(
    PluginAutomationTemplateId Id,
    PluginFeatureId FeatureId,
    string Name,
    ImmutableArray<PluginAutomationTemplateNode> Nodes,
    ImmutableArray<PluginAutomationTemplateEdge> Edges
);

public sealed record PluginAutomationTemplateNode(
    PluginTemplateNodeId Id,
    PluginAutomationDefinitionId DefinitionId,
    PluginValue Configuration
);

public sealed record PluginAutomationTemplateEdge(
    PluginTemplateNodeId FromNode,
    PluginAutomationFieldId FromOutput,
    PluginTemplateNodeId ToNode,
    PluginAutomationFieldId ToInput
);
