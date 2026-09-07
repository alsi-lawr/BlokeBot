using System.Text.Json.Serialization;
using BlokeBot.Core.Features.Automations;

namespace BlokeBot.Core.Features.ConfigurationTransfer.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AutomationSubflowV2(
    [property: JsonRequired] string Id,
    [property: JsonRequired] string Description,
    [property: JsonRequired] AutomationSubflowInterface Interface,
    [property: JsonRequired] AutomationFlowV2 Graph
);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AutomationSubflowBindingV2(
    [property: JsonRequired] string? SubflowId,
    [property: JsonRequired] AutomationSubflowInterface Interface,
    [property: JsonRequired] string FixedInputsJson
);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AutomationPluginContractV2(
    [property: JsonRequired] string PluginId,
    [property: JsonRequired] string Version,
    [property: JsonRequired] string Tag,
    [property: JsonRequired] int ManifestVersion,
    [property: JsonRequired] string FeatureId,
    [property: JsonRequired] string DefinitionId,
    [property: JsonRequired] string DefinitionHash
)
{
    internal static bool Available(IPluginAutomationDefinition plugin) =>
        plugin.Endpoint.State.Enabled
        && (
            plugin.Endpoint.State.Readiness
                is BlokeBot.Plugins.Features.PluginFeatureReadiness.Ready
            || plugin.Endpoint.Declaration.FindFeature(plugin.Endpoint.State.Key.FeatureId)?.Twitch
                is { Scopes.IsEmpty: true, EventSubTypes.IsEmpty: true }
        );

    internal static AutomationPluginContractV2 From(AutomationPluginProvenance value) =>
        new(
            value.PluginId,
            value.PluginVersion,
            value.MutableTag,
            value.ManifestVersion,
            value.FeatureId,
            value.DefinitionId,
            value.DefinitionHash
        );
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AutomationScenarioV2(
    [property: JsonRequired] string Id,
    [property: JsonRequired] string FlowId,
    [property: JsonRequired] string Name,
    [property: JsonRequired] string SourceNodeId,
    [property: JsonRequired] string SourceDefinitionId,
    [property: JsonRequired] int SourceSchemaVersion,
    [property: JsonRequired] DateTimeOffset ClockUtc,
    [property: JsonRequired] ulong Seed,
    [property: JsonRequired] IReadOnlyList<AutomationGeneratedInputV2> Inputs,
    [property: JsonRequired] IReadOnlyList<AutomationScenarioEffectV2> Effects
);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AutomationGeneratedInputV2(
    [property: JsonRequired] string NodeId,
    [property: JsonRequired] string PortId,
    [property: JsonRequired] AutomationPortValueType ValueType,
    [property: JsonRequired] AutomationPortNullability Nullability,
    [property: JsonRequired] AutomationDataSensitivity Sensitivity,
    [property: JsonRequired] AutomationValueProvenance Provenance
);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AutomationScenarioEffectV2(
    [property: JsonRequired] string NodeId,
    [property: JsonRequired] AutomationScenarioEffectResult Result
);
