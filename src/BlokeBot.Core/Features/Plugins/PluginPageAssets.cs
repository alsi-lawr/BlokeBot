using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Features;
using BlokeBot.Plugins.Runtime;

namespace BlokeBot.Core.Features.Plugins;

internal sealed record PluginPageAsset(
    ReadOnlyMemory<byte> Content,
    string MediaType,
    bool IsDocument
);

internal abstract record PluginPageAssetResolution
{
    private PluginPageAssetResolution() { }

    internal sealed record Available(PluginPageAsset Asset) : PluginPageAssetResolution;

    internal sealed record NotFound : PluginPageAssetResolution;

    internal sealed record TooLarge : PluginPageAssetResolution;
}

internal sealed class UnavailablePluginPackageAssetResolver : IPluginPackageAssetResolver
{
    public ValueTask<PluginPackageAssetResolution> ResolveAsync(
        PluginInstallationIdentity installation,
        CancellationToken cancellationToken
    ) =>
        ValueTask.FromResult<PluginPackageAssetResolution>(
            new PluginPackageAssetResolution.Unavailable()
        );
}

internal sealed class PluginPageAssetService(
    PluginPageCatalog pages,
    IPluginPackageAssetResolver packages
)
{
    internal async ValueTask<PluginPageAssetResolution> ResolveAsync(
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
            return new PluginPageAssetResolution.NotFound();
        }

        var allowed = embedded
            .Descriptor.Assets.Append(embedded.Descriptor.DocumentAsset)
            .ToHashSet();
        var asset = embedded.Declaration.Manifest.Assets.FirstOrDefault(candidate =>
            allowed.Contains(candidate.Id)
            && string.Equals(candidate.Path, assetPath, StringComparison.Ordinal)
        );
        if (asset is null)
        {
            return new PluginPageAssetResolution.NotFound();
        }

        var packageResolution = await packages.ResolveAsync(
            embedded.Declaration.Installation,
            cancellationToken
        );
        if (
            packageResolution is not PluginPackageAssetResolution.Available package
            || package.Manifest.Manifest.Id != pluginId
            || package.Manifest.Manifest.Release != embedded.Declaration.Installation.Release
            || package.Manifest.Manifest.Assets.FirstOrDefault(candidate =>
                candidate.Id == asset.Id
            )
                is not { } packagedAsset
            || !SameAsset(asset, packagedAsset)
        )
        {
            return new PluginPageAssetResolution.NotFound();
        }

        var fullRoot = Path.GetFullPath(package.PackageRoot);
        var fullPath = Path.GetFullPath(
            Path.Combine(fullRoot, asset.Path.Replace('/', Path.DirectorySeparatorChar))
        );
        var prefix = fullRoot.EndsWith(Path.DirectorySeparatorChar)
            ? fullRoot
            : $"{fullRoot}{Path.DirectorySeparatorChar}";
        if (!fullPath.StartsWith(prefix, StringComparison.Ordinal) || !File.Exists(fullPath))
        {
            return new PluginPageAssetResolution.NotFound();
        }

        var globalLimit =
            asset.Kind is PluginAssetKind.Browser
                ? PluginContractLimits.MaximumBrowserAssetBytes
                : PluginContractLimits.MaximumMediaAssetBytes;
        var maximumBytes = Math.Min(asset.MaximumBytes, globalLimit);
        await using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan
        );
        if (stream.Length > maximumBytes)
        {
            return new PluginPageAssetResolution.TooLarge();
        }
        using var output = new MemoryStream((int)stream.Length);
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }
            if (output.Length + read > maximumBytes)
            {
                return new PluginPageAssetResolution.TooLarge();
            }
            output.Write(buffer, 0, read);
        }

        return (
            pages.Resolve(pluginId, featureId, hostId, route)
                is not PluginPageResolution.Available current
            || PluginPageSessionBinding.From(current.Endpoint)
                != PluginPageSessionBinding.From(endpoint)
        )
            ? new PluginPageAssetResolution.NotFound()
            : new PluginPageAssetResolution.Available(
                new(
                    output.ToArray(),
                    asset.MediaType,
                    asset.Id == embedded.Descriptor.DocumentAsset
                )
            );
    }

    private static bool SameAsset(PluginAssetDescriptor expected, PluginAssetDescriptor actual) =>
        expected.Id == actual.Id
        && expected.Path == actual.Path
        && expected.Kind == actual.Kind
        && expected.MediaType.Equals(actual.MediaType, StringComparison.OrdinalIgnoreCase)
        && expected.Purpose == actual.Purpose
        && expected.RuntimeIdentifiers.SequenceEqual(actual.RuntimeIdentifiers)
        && expected.MaximumBytes == actual.MaximumBytes;
}
