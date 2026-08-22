using System.Collections.Immutable;

namespace BlokeBot.Plugins.Contracts;

public sealed record PluginManifest(
    int ManifestVersion,
    PluginId Id,
    string Name,
    string Description,
    PluginReleaseIdentity Release,
    PluginCompatibilityDeclaration Compatibility,
    PluginLuaModuleId EntryModule,
    ImmutableArray<PluginLuaModuleDescriptor> LuaModules,
    ImmutableArray<PluginAssetDescriptor> Assets,
    ImmutableArray<PluginPayloadDescriptor> Payloads,
    ImmutableArray<PluginSettingDescriptor> Settings,
    ImmutableArray<PluginFeatureDescriptor> Features,
    ImmutableArray<PluginHostModuleRequirement> HostModules,
    ImmutableArray<PluginMigrationDescriptor> Migrations,
    ImmutableArray<PluginAutomationDefinitionDescriptor> AutomationDefinitions,
    ImmutableArray<PluginAutomationTemplateDescriptor> AutomationTemplates,
    ImmutableArray<PluginGeneratedPageDescriptor> GeneratedPages,
    ImmutableArray<PluginEmbeddedPageDescriptor> EmbeddedPages
);

public enum PluginLuaVersion
{
    Lua54,
}

public sealed record PluginCompatibilityDeclaration(
    PluginApiVersion MinimumApiVersion,
    PluginApiVersion MaximumApiVersion,
    SemanticVersion MinimumBlokeBotVersion,
    SemanticVersion MaximumBlokeBotVersionExclusive,
    PluginLuaVersion LuaVersion
);

public sealed record PluginLuaModuleDescriptor(PluginLuaModuleId Id, string Path);

public sealed record PluginMigrationDescriptor(
    PluginMigrationId Id,
    SemanticVersion FromVersion,
    SemanticVersion ToVersion,
    PluginLuaModuleId Module,
    string EntryPoint
);
