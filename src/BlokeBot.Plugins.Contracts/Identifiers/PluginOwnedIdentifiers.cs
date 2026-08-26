using System.Text.Json.Serialization;
using Tomlyn.Serialization;

namespace BlokeBot.Plugins.Contracts;

[JsonConverter(typeof(PluginContractIdentifierJsonConverter<PluginId>))]
[TomlConverter(typeof(PluginContractIdentifierTomlConverter<PluginId>))]
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
[TomlConverter(typeof(PluginContractIdentifierTomlConverter<PluginSettingId>))]
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
[TomlConverter(typeof(PluginContractIdentifierTomlConverter<PluginSettingChoiceId>))]
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
[TomlConverter(typeof(PluginContractIdentifierTomlConverter<PluginFeatureId>))]
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

[JsonConverter(typeof(PluginContractIdentifierJsonConverter<PluginEventHandlerId>))]
[TomlConverter(typeof(PluginContractIdentifierTomlConverter<PluginEventHandlerId>))]
public sealed record PluginEventHandlerId
    : PluginContractIdentifier,
        IPluginContractIdentifier<PluginEventHandlerId>
{
    private PluginEventHandlerId(string value)
        : base(value) { }

    public static bool TryCreate(string? candidate, out PluginEventHandlerId identifier) =>
        PluginContractIdentifierSyntax.TryCreate(
            candidate,
            static value => new PluginEventHandlerId(value),
            out identifier
        );
}

[JsonConverter(typeof(PluginContractIdentifierJsonConverter<PluginScheduleHandlerId>))]
[TomlConverter(typeof(PluginContractIdentifierTomlConverter<PluginScheduleHandlerId>))]
public sealed record PluginScheduleHandlerId
    : PluginContractIdentifier,
        IPluginContractIdentifier<PluginScheduleHandlerId>
{
    private PluginScheduleHandlerId(string value)
        : base(value) { }

    public static bool TryCreate(string? candidate, out PluginScheduleHandlerId identifier) =>
        PluginContractIdentifierSyntax.TryCreate(
            candidate,
            static value => new PluginScheduleHandlerId(value),
            out identifier
        );
}

[JsonConverter(typeof(PluginContractIdentifierJsonConverter<PluginWebhookId>))]
[TomlConverter(typeof(PluginContractIdentifierTomlConverter<PluginWebhookId>))]
public sealed record PluginWebhookId
    : PluginContractIdentifier,
        IPluginContractIdentifier<PluginWebhookId>
{
    private PluginWebhookId(string value)
        : base(value) { }

    public static bool TryCreate(string? candidate, out PluginWebhookId identifier) =>
        PluginContractIdentifierSyntax.TryCreate(
            candidate,
            static value => new PluginWebhookId(value),
            out identifier
        );
}

[JsonConverter(typeof(PluginContractIdentifierJsonConverter<PluginActionId>))]
[TomlConverter(typeof(PluginContractIdentifierTomlConverter<PluginActionId>))]
public sealed record PluginActionId
    : PluginContractIdentifier,
        IPluginContractIdentifier<PluginActionId>
{
    private PluginActionId(string value)
        : base(value) { }

    public static bool TryCreate(string? candidate, out PluginActionId identifier) =>
        PluginContractIdentifierSyntax.TryCreate(
            candidate,
            static value => new PluginActionId(value),
            out identifier
        );
}

[JsonConverter(typeof(PluginContractIdentifierJsonConverter<PluginMigrationId>))]
[TomlConverter(typeof(PluginContractIdentifierTomlConverter<PluginMigrationId>))]
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
[TomlConverter(typeof(PluginContractIdentifierTomlConverter<PluginLuaModuleId>))]
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
[TomlConverter(typeof(PluginContractIdentifierTomlConverter<PluginAutomationDefinitionId>))]
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
[TomlConverter(typeof(PluginContractIdentifierTomlConverter<PluginAutomationTemplateId>))]
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
[TomlConverter(typeof(PluginContractIdentifierTomlConverter<PluginTemplateNodeId>))]
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
[TomlConverter(typeof(PluginContractIdentifierTomlConverter<PluginPageId>))]
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
[TomlConverter(typeof(PluginContractIdentifierTomlConverter<PluginAssetId>))]
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
[TomlConverter(typeof(PluginContractIdentifierTomlConverter<PluginPayloadId>))]
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
[TomlConverter(typeof(PluginContractIdentifierTomlConverter<PluginAutomationFieldId>))]
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
