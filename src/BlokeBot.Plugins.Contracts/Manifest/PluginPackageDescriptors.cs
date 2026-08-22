using System.Collections.Immutable;

namespace BlokeBot.Plugins.Contracts;

public enum PluginAssetKind
{
    Browser,
    Media,
}

public sealed record PluginAssetDescriptor(
    PluginAssetId Id,
    string Path,
    PluginAssetKind Kind,
    string MediaType,
    string Purpose,
    ImmutableArray<PluginRuntimeIdentifier> RuntimeIdentifiers,
    long MaximumBytes
);

public sealed record PluginPayloadDescriptor(
    PluginPayloadId Id,
    string Path,
    string Purpose,
    ImmutableArray<PluginRuntimeIdentifier> RuntimeIdentifiers,
    long MaximumBytes
);
