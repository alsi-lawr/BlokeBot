using System.Text.Json.Serialization;

namespace BlokeBot.Plugins.Contracts;

[JsonConverter(typeof(PluginContractIdentifierJsonConverter<PluginId>))]
public sealed record PluginId : PluginContractIdentifier, IPluginContractIdentifier<PluginId>
{
    private PluginId(string value)
        : base(value) { }

    public static bool TryCreate(string? candidate, out PluginId identifier) =>
        PluginContractIdentifierSyntax.TryCreate(
            candidate,
            static value => new PluginId(value),
            out identifier
        );
}

[JsonConverter(typeof(PluginContractIdentifierJsonConverter<PluginSettingId>))]
public sealed record PluginSettingId
    : PluginContractIdentifier,
        IPluginContractIdentifier<PluginSettingId>
{
    private PluginSettingId(string value)
        : base(value) { }

    public static bool TryCreate(string? candidate, out PluginSettingId identifier) =>
        PluginContractIdentifierSyntax.TryCreate(
            candidate,
            static value => new PluginSettingId(value),
            out identifier
        );
}

[JsonConverter(typeof(PluginContractIdentifierJsonConverter<PluginSettingChoiceId>))]
public sealed record PluginSettingChoiceId
    : PluginContractIdentifier,
        IPluginContractIdentifier<PluginSettingChoiceId>
{
    private PluginSettingChoiceId(string value)
        : base(value) { }

    public static bool TryCreate(string? candidate, out PluginSettingChoiceId identifier) =>
        PluginContractIdentifierSyntax.TryCreate(
            candidate,
            static value => new PluginSettingChoiceId(value),
            out identifier
        );
}

[JsonConverter(typeof(PluginContractIdentifierJsonConverter<PluginFeatureId>))]
public sealed record PluginFeatureId
    : PluginContractIdentifier,
        IPluginContractIdentifier<PluginFeatureId>
{
    private PluginFeatureId(string value)
        : base(value) { }

    public static bool TryCreate(string? candidate, out PluginFeatureId identifier) =>
        PluginContractIdentifierSyntax.TryCreate(
            candidate,
            static value => new PluginFeatureId(value),
            out identifier
        );
}

[JsonConverter(typeof(PluginContractIdentifierJsonConverter<PluginMigrationId>))]
public sealed record PluginMigrationId
    : PluginContractIdentifier,
        IPluginContractIdentifier<PluginMigrationId>
{
    private PluginMigrationId(string value)
        : base(value) { }

    public static bool TryCreate(string? candidate, out PluginMigrationId identifier) =>
        PluginContractIdentifierSyntax.TryCreate(
            candidate,
            static value => new PluginMigrationId(value),
            out identifier
        );
}

[JsonConverter(typeof(PluginContractIdentifierJsonConverter<PluginLuaModuleId>))]
public sealed record PluginLuaModuleId
    : PluginContractIdentifier,
        IPluginContractIdentifier<PluginLuaModuleId>
{
    private PluginLuaModuleId(string value)
        : base(value) { }

    public static bool TryCreate(string? candidate, out PluginLuaModuleId identifier) =>
        PluginContractIdentifierSyntax.TryCreate(
            candidate,
            static value => new PluginLuaModuleId(value),
            out identifier
        );
}

[JsonConverter(typeof(PluginContractIdentifierJsonConverter<PluginAutomationDefinitionId>))]
public sealed record PluginAutomationDefinitionId
    : PluginContractIdentifier,
        IPluginContractIdentifier<PluginAutomationDefinitionId>
{
    private PluginAutomationDefinitionId(string value)
        : base(value) { }

    public static bool TryCreate(string? candidate, out PluginAutomationDefinitionId identifier) =>
        PluginContractIdentifierSyntax.TryCreate(
            candidate,
            static value => new PluginAutomationDefinitionId(value),
            out identifier
        );
}

[JsonConverter(typeof(PluginContractIdentifierJsonConverter<PluginAutomationTemplateId>))]
public sealed record PluginAutomationTemplateId
    : PluginContractIdentifier,
        IPluginContractIdentifier<PluginAutomationTemplateId>
{
    private PluginAutomationTemplateId(string value)
        : base(value) { }

    public static bool TryCreate(string? candidate, out PluginAutomationTemplateId identifier) =>
        PluginContractIdentifierSyntax.TryCreate(
            candidate,
            static value => new PluginAutomationTemplateId(value),
            out identifier
        );
}

[JsonConverter(typeof(PluginContractIdentifierJsonConverter<PluginTemplateNodeId>))]
public sealed record PluginTemplateNodeId
    : PluginContractIdentifier,
        IPluginContractIdentifier<PluginTemplateNodeId>
{
    private PluginTemplateNodeId(string value)
        : base(value) { }

    public static bool TryCreate(string? candidate, out PluginTemplateNodeId identifier) =>
        PluginContractIdentifierSyntax.TryCreate(
            candidate,
            static value => new PluginTemplateNodeId(value),
            out identifier
        );
}

[JsonConverter(typeof(PluginContractIdentifierJsonConverter<PluginPageId>))]
public sealed record PluginPageId
    : PluginContractIdentifier,
        IPluginContractIdentifier<PluginPageId>
{
    private PluginPageId(string value)
        : base(value) { }

    public static bool TryCreate(string? candidate, out PluginPageId identifier) =>
        PluginContractIdentifierSyntax.TryCreate(
            candidate,
            static value => new PluginPageId(value),
            out identifier
        );
}

[JsonConverter(typeof(PluginContractIdentifierJsonConverter<PluginAssetId>))]
public sealed record PluginAssetId
    : PluginContractIdentifier,
        IPluginContractIdentifier<PluginAssetId>
{
    private PluginAssetId(string value)
        : base(value) { }

    public static bool TryCreate(string? candidate, out PluginAssetId identifier) =>
        PluginContractIdentifierSyntax.TryCreate(
            candidate,
            static value => new PluginAssetId(value),
            out identifier
        );
}

[JsonConverter(typeof(PluginContractIdentifierJsonConverter<PluginPayloadId>))]
public sealed record PluginPayloadId
    : PluginContractIdentifier,
        IPluginContractIdentifier<PluginPayloadId>
{
    private PluginPayloadId(string value)
        : base(value) { }

    public static bool TryCreate(string? candidate, out PluginPayloadId identifier) =>
        PluginContractIdentifierSyntax.TryCreate(
            candidate,
            static value => new PluginPayloadId(value),
            out identifier
        );
}

[JsonConverter(typeof(PluginContractIdentifierJsonConverter<PluginAutomationFieldId>))]
public sealed record PluginAutomationFieldId
    : PluginContractIdentifier,
        IPluginContractIdentifier<PluginAutomationFieldId>
{
    private PluginAutomationFieldId(string value)
        : base(value) { }

    public static bool TryCreate(string? candidate, out PluginAutomationFieldId identifier) =>
        PluginContractIdentifierSyntax.TryCreate(
            candidate,
            static value => new PluginAutomationFieldId(value),
            out identifier
        );
}
