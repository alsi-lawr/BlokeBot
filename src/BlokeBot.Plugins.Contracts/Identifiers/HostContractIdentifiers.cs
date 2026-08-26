using System.Text.Json.Serialization;
using Tomlyn.Serialization;

namespace BlokeBot.Plugins.Contracts;

[JsonConverter(typeof(PluginContractIdentifierJsonConverter<PluginHostModuleId>))]
[TomlConverter(typeof(PluginContractIdentifierTomlConverter<PluginHostModuleId>))]
public sealed record PluginHostModuleId
    : PluginContractIdentifier,
        IPluginContractIdentifier<PluginHostModuleId>
{
    private PluginHostModuleId(string value)
        : base(value) { }

    public static bool TryCreate(string? candidate, out PluginHostModuleId identifier) =>
        PluginContractIdentifierSyntax.TryCreate(
            candidate,
            static value => new PluginHostModuleId(value),
            out identifier
        );
}

[JsonConverter(typeof(PluginContractIdentifierJsonConverter<PluginHostOperationId>))]
[TomlConverter(typeof(PluginContractIdentifierTomlConverter<PluginHostOperationId>))]
public sealed record PluginHostOperationId
    : PluginContractIdentifier,
        IPluginContractIdentifier<PluginHostOperationId>
{
    private PluginHostOperationId(string value)
        : base(value) { }

    public static bool TryCreate(string? candidate, out PluginHostOperationId identifier) =>
        PluginContractIdentifierSyntax.TryCreate(
            candidate,
            static value => new PluginHostOperationId(value),
            out identifier
        );
}

[JsonConverter(typeof(PluginContractIdentifierJsonConverter<PluginEngineId>))]
[TomlConverter(typeof(PluginContractIdentifierTomlConverter<PluginEngineId>))]
public sealed record PluginEngineId
    : PluginContractIdentifier,
        IPluginContractIdentifier<PluginEngineId>
{
    private PluginEngineId(string value)
        : base(value) { }

    public static bool TryCreate(string? candidate, out PluginEngineId identifier) =>
        PluginContractIdentifierSyntax.TryCreate(
            candidate,
            static value => new PluginEngineId(value),
            out identifier
        );
}
