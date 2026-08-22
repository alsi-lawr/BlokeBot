using System.Collections.Immutable;

namespace BlokeBot.Plugins.Contracts;

public sealed record PluginGeneratedPageDescriptor(
    PluginPageId Id,
    PluginFeatureId FeatureId,
    string Route,
    string Title,
    PluginLuaModuleId Module,
    string RenderEntryPoint
);

public sealed record PluginEmbeddedPageDescriptor(
    PluginPageId Id,
    PluginFeatureId FeatureId,
    string Route,
    string Title,
    PluginAssetId DocumentAsset,
    ImmutableArray<PluginAssetId> Assets,
    ImmutableArray<Uri> MessageOrigins
);
