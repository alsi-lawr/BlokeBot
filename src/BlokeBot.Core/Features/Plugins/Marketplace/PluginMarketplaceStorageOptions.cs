namespace BlokeBot.Core.Features.Plugins;

public sealed record PluginMarketplaceStorageOptions(
    string PackageStateRoot,
    string PluginPrivateStateRoot,
    TimeSpan RefreshInterval
);
