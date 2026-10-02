using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Features;
using BlokeBot.Plugins.Runtime;

namespace BlokeBot.Core.Features.Plugins;

internal sealed record PluginAssetContent(ReadOnlyMemory<byte> Content, string MediaType);

internal abstract record PluginAssetContentResolution
{
    private PluginAssetContentResolution() { }

    public abstract TResult Match<TResult>(
        Func<Available, TResult> available,
        Func<NotFound, TResult> notFound,
        Func<TooLarge, TResult> tooLarge
    );

    internal sealed record Available(PluginAssetContent Asset) : PluginAssetContentResolution
    {
        public override TResult Match<TResult>(
            Func<Available, TResult> available,
            Func<NotFound, TResult> notFound,
            Func<TooLarge, TResult> tooLarge
        ) => available(this);
    }

    internal sealed record NotFound : PluginAssetContentResolution
    {
        public override TResult Match<TResult>(
            Func<Available, TResult> available,
            Func<NotFound, TResult> notFound,
            Func<TooLarge, TResult> tooLarge
        ) => notFound(this);
    }

    internal sealed record TooLarge : PluginAssetContentResolution
    {
        public override TResult Match<TResult>(
            Func<Available, TResult> available,
            Func<NotFound, TResult> notFound,
            Func<TooLarge, TResult> tooLarge
        ) => tooLarge(this);
    }
}

internal sealed class PluginDeclaredAssetReader(IPluginPackageAssetResolver packages)
{
    internal async ValueTask<PluginAssetContentResolution> ReadAsync(
        PluginFeatureDeclaration declaration,
        PluginAssetDescriptor asset,
        Func<bool> isCurrent,
        CancellationToken cancellationToken
    )
    {
        var packageResolution = await packages.ResolveAsync(
            declaration.Installation,
            declaration.PackageOperationId,
            cancellationToken
        );
        if (
            packageResolution is not PluginPackageAssetResolution.Available package
            || package.Manifest.Manifest.Id != declaration.Installation.PluginId
            || package.Manifest.Manifest.Release != declaration.Installation.Release
            || package.Manifest.Manifest.Assets.FirstOrDefault(candidate =>
                candidate.Id == asset.Id
            )
                is not { } packagedAsset
            || !SameAsset(asset, packagedAsset)
        )
        {
            return new PluginAssetContentResolution.NotFound();
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
            return new PluginAssetContentResolution.NotFound();
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
            return new PluginAssetContentResolution.TooLarge();
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
                return new PluginAssetContentResolution.TooLarge();
            }
            output.Write(buffer, 0, read);
        }

        return !isCurrent()
            ? new PluginAssetContentResolution.NotFound()
            : new PluginAssetContentResolution.Available(new(output.ToArray(), asset.MediaType));
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
