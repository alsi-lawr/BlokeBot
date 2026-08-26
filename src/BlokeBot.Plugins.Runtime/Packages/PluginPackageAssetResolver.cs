using BlokeBot.Plugins.Contracts;

namespace BlokeBot.Plugins.Runtime;

public abstract record PluginPackageAssetResolution
{
    private PluginPackageAssetResolution() { }

    public sealed record Available(ValidatedPluginManifest Manifest, string PackageRoot)
        : PluginPackageAssetResolution;

    public sealed record Unavailable : PluginPackageAssetResolution;
}

public interface IPluginPackageAssetResolver
{
    ValueTask<PluginPackageAssetResolution> ResolveAsync(
        PluginInstallationIdentity installation,
        PluginLifecycleFence fence,
        CancellationToken cancellationToken
    );
}

internal sealed class LifecyclePluginPackageAssetResolver(IPluginLifecyclePackageResolver packages)
    : IPluginPackageAssetResolver
{
    public async ValueTask<PluginPackageAssetResolution> ResolveAsync(
        PluginInstallationIdentity installation,
        PluginLifecycleFence fence,
        CancellationToken cancellationToken
    )
    {
        var resolution = await packages.ResolveAsync(
            installation,
            fence.OperationId,
            cancellationToken
        );
        return
            resolution
                is PluginLifecyclePackageResolution.Available
                {
                    Package: { MatchesIdentity: true } package,
                }
            && package.Installation == installation
            && package.PreparedPackage.Descriptor.Plugin == installation
            && package.PreparedPackage.Manifest is { } manifest
            && manifest.Manifest.Id == installation.PluginId
            && manifest.Manifest.Release == installation.Release
            ? new PluginPackageAssetResolution.Available(
                manifest,
                package.PreparedPackage.PackageRoot
            )
            : new PluginPackageAssetResolution.Unavailable();
    }
}
