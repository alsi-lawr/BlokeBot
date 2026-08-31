using System.Collections.Immutable;

namespace BlokeBot.Plugins.Contracts;

public sealed record PluginHostModuleRequirement(
    PluginHostModuleId Id,
    PluginApiVersion MinimumVersion,
    PluginApiVersion MaximumVersion
);

public enum PluginInvocationContextKind
{
    Installation,
    Channel,
    Automation,
    Migration,
    Page,
}

public sealed record PluginHostModuleDescriptor(
    PluginHostModuleId Id,
    PluginApiVersion Version,
    ImmutableArray<PluginHostOperationDescriptor> Operations
);

public enum PluginLuaValueShape
{
    Nil,
    Boolean,
    Number,
    String,
    ValueArray,
    ValueMap,
    Context,
    InstallationSettings,
    FeatureSettings,
    DiagnosticLevel,
    OverlayTargetId,
    OverlayCueId,
    PointAmount,
    PointBalance,
    ScheduleInput,
    ScheduleIntervalSeconds,
    ScheduleId,
    SqlParameters,
    SqlRows,
    HttpRequest,
    HttpOutcome,
}

public sealed record PluginHostParameterDescriptor(
    string Name,
    PluginLuaValueShape Shape,
    string Description
);

public sealed record PluginHostOperationDescriptor(
    PluginHostOperationId Id,
    string LuaFunctionName,
    string Description,
    ImmutableArray<PluginInvocationContextKind> PermittedContexts,
    ImmutableArray<PluginHostParameterDescriptor> Parameters,
    PluginLuaValueShape ResultShape,
    string ResultDescription,
    int MaximumArgumentBytes,
    int MaximumResultBytes
)
{
    public ImmutableArray<PluginValueKind> ArgumentKinds { get; } =
    [.. Parameters.Select(parameter => PluginLuaValueShapes.Kind(parameter.Shape))];

    public PluginValueKind ResultKind { get; } = PluginLuaValueShapes.Kind(ResultShape);
}

public static class PluginLuaValueShapes
{
    public static PluginValueKind Kind(PluginLuaValueShape shape) =>
        shape switch
        {
            PluginLuaValueShape.Nil => PluginValueKind.Nil,
            PluginLuaValueShape.Boolean => PluginValueKind.Boolean,
            PluginLuaValueShape.Number => PluginValueKind.Number,
            PluginLuaValueShape.String
            or PluginLuaValueShape.DiagnosticLevel
            or PluginLuaValueShape.OverlayTargetId
            or PluginLuaValueShape.OverlayCueId
            or PluginLuaValueShape.PointAmount
            or PluginLuaValueShape.PointBalance
            or PluginLuaValueShape.ScheduleId => PluginValueKind.String,
            PluginLuaValueShape.ValueArray or PluginLuaValueShape.SqlRows => PluginValueKind.Array,
            PluginLuaValueShape.ValueMap
            or PluginLuaValueShape.Context
            or PluginLuaValueShape.InstallationSettings
            or PluginLuaValueShape.FeatureSettings
            or PluginLuaValueShape.ScheduleInput
            or PluginLuaValueShape.SqlParameters
            or PluginLuaValueShape.HttpRequest
            or PluginLuaValueShape.HttpOutcome => PluginValueKind.Map,
            PluginLuaValueShape.ScheduleIntervalSeconds => PluginValueKind.Number,
        };
}
