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
    long MaximumBytes
);
