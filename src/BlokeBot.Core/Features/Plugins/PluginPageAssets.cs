using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Features;
using BlokeBot.Plugins.Runtime;

namespace BlokeBot.Core.Features.Plugins;

internal sealed class UnavailablePluginPackageAssetResolver : IPluginPackageAssetResolver
{
    public ValueTask<PluginPackageAssetResolution> ResolveAsync(
        PluginInstallationIdentity installation,
        PluginPackageOperationId packageOperationId,
        CancellationToken cancellationToken
    ) =>
        ValueTask.FromResult<PluginPackageAssetResolution>(
            new PluginPackageAssetResolution.Unavailable()
        );
}

internal sealed class PluginPageAssetService(
    PluginPageCatalog pages,
    PluginDeclaredAssetReader assets
)
{
    internal async ValueTask<PluginAssetContentResolution> ResolveAsync(
        PluginId pluginId,
        PluginFeatureId featureId,
        PluginHostId hostId,
        string route,
        string assetPath,
        CancellationToken cancellationToken
    )
    {
        if (
            pages.Resolve(pluginId, featureId, hostId, route)
            is not PluginPageResolution.Available
            {
                Endpoint: { Definition: PluginPageDefinition.Embedded embedded } endpoint,
            }
        )
        {
            return new PluginAssetContentResolution.NotFound();
        }

        var allowed = embedded
            .Descriptor.Assets.Append(embedded.Descriptor.DocumentAsset)
            .ToHashSet();
        var asset = embedded.Declaration.Manifest.Assets.FirstOrDefault(candidate =>
            allowed.Contains(candidate.Id)
            && string.Equals(candidate.Path, assetPath, StringComparison.Ordinal)
        );
        return asset is null
            ? new PluginAssetContentResolution.NotFound()
            : await assets.ReadAsync(
                embedded.Declaration,
                asset,
                () =>
                    pages.Resolve(pluginId, featureId, hostId, route)
                        is PluginPageResolution.Available current
                    && PluginPageSessionBinding.From(current.Endpoint)
                        == PluginPageSessionBinding.From(endpoint),
                cancellationToken
            );
    }
}
