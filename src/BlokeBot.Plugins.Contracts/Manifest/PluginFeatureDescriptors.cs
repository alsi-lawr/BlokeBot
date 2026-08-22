using System.Collections.Immutable;

namespace BlokeBot.Plugins.Contracts;

public enum PluginSettingScope
{
    Installation,
    Channel,
}

public enum PluginSettingValueKind
{
    Boolean,
    Integer,
    Number,
    String,
    Secret,
    Choice,
}

public sealed record PluginSettingDescriptor(
    PluginSettingId Id,
    string Name,
    string Description,
    PluginSettingScope Scope,
    PluginSettingValueKind ValueKind,
    bool Required,
    ImmutableArray<string> Choices
);

public sealed record PluginFeatureDescriptor(
    PluginFeatureId Id,
    string Name,
    string Description,
    ImmutableArray<PluginSettingId> Settings,
    PluginTwitchRequirements Twitch,
    ImmutableArray<PluginAutomationTemplateId> AutomationTemplates
);

public sealed record PluginTwitchRequirements(
    ImmutableArray<string> Scopes,
    ImmutableArray<string> EventSubTypes
);
