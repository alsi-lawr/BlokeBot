using BlokeBot.Plugins.Features;

namespace BlokeBot.Core.Features.Plugins;

internal sealed class PluginWidgetAssetService(
    PluginWidgetCatalog widgets,
    PluginDeclaredAssetReader assets
)
{
    internal ValueTask<PluginAssetContentResolution> ResolveAsync(
        PluginWidgetEndpoint endpoint,
        string assetPath,
        Func<bool> overlayIsCurrent,
        CancellationToken cancellationToken
    )
    {
        var allowed = endpoint
            .Descriptor.Assets.Append(endpoint.Descriptor.DocumentAsset)
            .ToHashSet();
        var asset = endpoint.Declaration.Manifest.Assets.FirstOrDefault(candidate =>
            allowed.Contains(candidate.Id) && candidate.Path == assetPath
        );
        return asset is null || !widgets.IsCurrent(endpoint) || !overlayIsCurrent()
            ? ValueTask.FromResult<PluginAssetContentResolution>(
                new PluginAssetContentResolution.NotFound()
            )
            : assets.ReadAsync(
                endpoint.Declaration,
                asset,
                () => widgets.IsCurrent(endpoint) && overlayIsCurrent(),
                cancellationToken
            );
    }
}
