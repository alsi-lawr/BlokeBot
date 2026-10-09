using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Tomlyn.Serialization;

namespace BlokeBot.Plugins.Contracts;

[JsonConverter(typeof(PluginContractIdentifierJsonConverter<PluginWidgetId>))]
[TomlConverter(typeof(PluginContractIdentifierTomlConverter<PluginWidgetId>))]
public sealed record PluginWidgetId
    : PluginContractIdentifier,
        IPluginContractIdentifier<PluginWidgetId>
{
    private PluginWidgetId(string value)
        : base(value) { }

    public static bool TryCreate(string? candidate, out PluginWidgetId identifier) =>
        PluginContractIdentifierSyntax.TryCreate(
            candidate,
            static value => new PluginWidgetId(value),
            out identifier
        );
}

// The server handler produces a public-safe map for the declared browser document. Its own
// settings/storage/HTTP capabilities are not browser authority or a management Page action bridge.
// Projection handlers honor preview intent without admitting or consuming production work.
public sealed record PluginWidgetDescriptor(
    PluginWidgetId Id,
    PluginFeatureId FeatureId,
    string Title,
    PluginLuaModuleId Module,
    string RenderEntryPoint,
    PluginAssetId DocumentAsset,
    ImmutableArray<PluginAssetId> Assets,
    ImmutableArray<PluginWidgetConfigurationField> ConfigurationFields,
    PluginValue DefaultConfiguration
);

public sealed record PluginWidgetConfigurationField(
    string Name,
    string Title,
    PluginValueKind ValueKind,
    bool Required
)
{
    // Absent metadata preserves local use but never implies configuration export safety.
    public PluginWidgetFieldPortability? Portability { get; init; }
}

public enum PluginWidgetFieldPortability
{
    Portable,
    DestinationBinding,
    Withheld,
}

public static class PluginWidgetConfiguration
{
    public static bool IsValid(PluginWidgetDescriptor descriptor, PluginValue configuration) =>
        configuration is PluginValue.Map map
        && PluginValueValidator.Validate(map) is PluginValueValidationOutcome.Valid
        && map.Properties.All(property =>
            descriptor.ConfigurationFields.Any(field =>
                field.Name == property.Name && field.ValueKind == property.Value.Kind
            )
        )
        && descriptor.ConfigurationFields.All(field =>
            !field.Required || map.Properties.Any(property => property.Name == field.Name)
        );
}
