using System.Text;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Features;
using BlokeBot.Plugins.Runtime;

namespace BlokeBot.Core.Features.Plugins;

internal enum PluginMarketplacePackageFailureCode
{
    DownloadFailed,
    ArchiveRejected,
    PackageRejected,
    IdentityMismatch,
    StagingFailed,
}

internal abstract record PluginMarketplacePackagePreparationOutcome
{
    private PluginMarketplacePackagePreparationOutcome() { }

    internal sealed record Prepared(PluginLifecyclePackage Package)
        : PluginMarketplacePackagePreparationOutcome;

    internal sealed record Rejected(PluginMarketplacePackageFailureCode Code)
        : PluginMarketplacePackagePreparationOutcome;
}

internal sealed class PluginMarketplacePackageStore(
    PluginMarketplaceStorageOptions options,
    IPluginMarketplaceArchiveTransport archives,
    PluginMarketplaceArchiveReader reader,
    PluginMarketplaceRuntimeContext runtime
) : IPluginRemovalDataOwner
{
    private readonly string _packageRoot = Path.GetFullPath(options.PackageStateRoot);

    internal async ValueTask<PluginMarketplacePackagePreparationOutcome> PrepareAsync(
        PluginMarketplaceCatalogEntry entry,
        PluginPackageOperationId packageOperationId,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!PluginMarketplaceCompatibilityPolicy.IsCompatible(entry.Compatibility, runtime.Target))
        {
            return new PluginMarketplacePackagePreparationOutcome.Rejected(
                PluginMarketplacePackageFailureCode.PackageRejected
            );
        }

        var installation = new PluginInstallationIdentity(entry.PluginId, entry.Release);
        var destination = PackageDirectory(installation, packageOperationId);
        var parent = Path.GetDirectoryName(destination)!;
        _ = Directory.CreateDirectory(parent);
        var nonce = Guid.NewGuid().ToString("N");
        var archivePath = Path.Combine(parent, $"archive.preparing-{nonce}.tar.gz");
        var preparing = $"{destination}.preparing-{nonce}";
        try
        {
            var archive = await archives.DownloadAsync(
                entry.RepositoryUrl,
                entry.Release.Tag,
                archivePath,
                cancellationToken
            );
            if (archive is not PluginMarketplaceArchiveDownload.Delivered)
            {
                return new PluginMarketplacePackagePreparationOutcome.Rejected(
                    PluginMarketplacePackageFailureCode.DownloadFailed
                );
            }

            _ = Directory.CreateDirectory(preparing);
            if (
                await reader.ExtractAsync(
                    archivePath,
                    entry.PackagePath,
                    preparing,
                    cancellationToken
                )
                is not PluginMarketplaceArchiveReadOutcome.Accepted
            )
            {
                return new PluginMarketplacePackagePreparationOutcome.Rejected(
                    PluginMarketplacePackageFailureCode.ArchiveRejected
                );
            }

            var validation = await PluginMarketplaceMaterializedPackageValidator.ValidateAsync(
                preparing,
                runtime.Target,
                cancellationToken
            );
            if (
                validation
                is not PluginMarketplaceMaterializedPackageValidationOutcome.Accepted accepted
            )
            {
                return new PluginMarketplacePackagePreparationOutcome.Rejected(
                    PluginMarketplacePackageFailureCode.PackageRejected
                );
            }

            if (!Matches(installation, accepted.Package))
            {
                return new PluginMarketplacePackagePreparationOutcome.Rejected(
                    PluginMarketplacePackageFailureCode.IdentityMismatch
                );
            }

            try
            {
                Directory.Move(preparing, destination);
            }
            catch (IOException)
            {
                var raced = await ResolveAsync(installation, packageOperationId, cancellationToken);
                return raced is PluginLifecyclePackageResolution.Available racedAvailable
                    ? new PluginMarketplacePackagePreparationOutcome.Prepared(
                        racedAvailable.Package
                    )
                    : new PluginMarketplacePackagePreparationOutcome.Rejected(
                        PluginMarketplacePackageFailureCode.StagingFailed
                    );
            }

            var prepared = accepted.Package with { PackageRoot = destination };
            return new PluginMarketplacePackagePreparationOutcome.Prepared(
                LifecyclePackage(installation, packageOperationId, prepared)
            );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            var raced = await ResolveAsync(installation, packageOperationId, cancellationToken);
            return raced is PluginLifecyclePackageResolution.Available racedAvailable
                ? new PluginMarketplacePackagePreparationOutcome.Prepared(racedAvailable.Package)
                : new PluginMarketplacePackagePreparationOutcome.Rejected(
                    PluginMarketplacePackageFailureCode.StagingFailed
                );
        }
        finally
        {
            DeleteFile(archivePath);
            DeleteDirectory(preparing);
            if (!Directory.Exists(destination))
            {
                DeleteDirectory(OperationDirectory(installation, packageOperationId));
            }
        }
    }

    internal async ValueTask<PluginLifecyclePackageResolution> ResolveAsync(
        PluginInstallationIdentity installation,
        PluginPackageOperationId packageOperationId,
        CancellationToken cancellationToken
    )
    {
        var root = PackageDirectory(installation, packageOperationId);
        return await ResolveAtAsync(installation, packageOperationId, root, cancellationToken);
    }

    internal ValueTask RemoveAsync(PluginId pluginId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DeleteDirectory(Path.Combine(_packageRoot, pluginId.Value));
        return ValueTask.CompletedTask;
    }

    internal ValueTask RemoveOperationAsync(
        PluginInstallationIdentity installation,
        PluginPackageOperationId packageOperationId,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        DeleteDirectory(OperationDirectory(installation, packageOperationId));
        return ValueTask.CompletedTask;
    }

    internal ValueTask RetainOnlyAsync(
        PluginInstallationIdentity installation,
        PluginPackageOperationId packageOperationId,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var retained = Path.GetFullPath(OperationDirectory(installation, packageOperationId));
        var pluginRoot = Path.Combine(_packageRoot, installation.PluginId.Value);
        if (!Directory.Exists(pluginRoot))
        {
            return ValueTask.CompletedTask;
        }

        foreach (
            var operationDirectory in Directory
                .EnumerateDirectories(pluginRoot, "*", SearchOption.AllDirectories)
                .Where(path =>
                    string.Equals(
                        Path.GetFileName(Path.GetDirectoryName(path)),
                        "operations",
                        StringComparison.Ordinal
                    )
                )
                .ToArray()
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (
                !string.Equals(
                    Path.GetFullPath(operationDirectory),
                    retained,
                    StringComparison.Ordinal
                )
            )
            {
                DeleteDirectory(operationDirectory);
            }
        }

        return ValueTask.CompletedTask;
    }

    internal ValueTask CleanupInterruptedAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_packageRoot))
        {
            return ValueTask.CompletedTask;
        }

        var interruptedFiles = Directory
            .EnumerateFiles(_packageRoot, "*.preparing-*", SearchOption.AllDirectories)
            .ToArray();
        var interruptedDirectories = Directory
            .EnumerateDirectories(_packageRoot, "*.preparing-*", SearchOption.AllDirectories)
            .OrderByDescending(static path => path.Length)
            .ToArray();
        foreach (var file in interruptedFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DeleteFile(file);
        }

        foreach (var directory in interruptedDirectories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DeleteDirectory(directory);
        }

        return ValueTask.CompletedTask;
    }

    public async ValueTask<PluginLifecycleOwnerOutcome> RemoveAsync(
        PluginRemovalContext context,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await RemoveAsync(context.PluginId, cancellationToken);
            return new PluginLifecycleOwnerOutcome.Succeeded();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new PluginLifecycleOwnerOutcome.Failed(
                PluginLifecycleOwnerFailureCode.Failed,
                SafeRemovalDetail()
            );
        }
    }

    private string PackageDirectory(
        PluginInstallationIdentity installation,
        PluginPackageOperationId packageOperationId
    ) => Path.Combine(OperationDirectory(installation, packageOperationId), "package");

    private string OperationDirectory(
        PluginInstallationIdentity installation,
        PluginPackageOperationId packageOperationId
    ) =>
        Path.Combine(
            _packageRoot,
            installation.PluginId.Value,
            installation.Release.DeclaredVersion.Value,
            EncodeTag(installation.Release.Tag.Value),
            "operations",
            packageOperationId.Value.ToString("N")
        );

    private PluginLifecyclePackage LifecyclePackage(
        PluginInstallationIdentity installation,
        PluginPackageOperationId packageOperationId,
        PreparedPluginWorkerPackage package
    ) =>
        new(
            installation,
            packageOperationId,
            package,
            Path.Combine(
                Path.GetFullPath(options.PluginPrivateStateRoot),
                installation.PluginId.Value
            ),
            runtime.HostCalls,
            runtime.WorkerLogger
        );

    private async ValueTask<PluginLifecyclePackageResolution> ResolveAtAsync(
        PluginInstallationIdentity installation,
        PluginPackageOperationId packageOperationId,
        string root,
        CancellationToken cancellationToken
    )
    {
        var validation = await PluginMarketplaceMaterializedPackageValidator.ValidateAsync(
            root,
            runtime.Target,
            cancellationToken
        );
        return
            validation
                is not PluginMarketplaceMaterializedPackageValidationOutcome.Accepted accepted
            || !Matches(installation, accepted.Package)
            ? new PluginLifecyclePackageResolution.Unavailable()
            : new PluginLifecyclePackageResolution.Available(
                LifecyclePackage(
                    installation,
                    packageOperationId,
                    accepted.Package with
                    {
                        PackageRoot = Path.GetFullPath(root),
                    }
                )
            );
    }

    private static bool Matches(
        PluginInstallationIdentity installation,
        PreparedPluginWorkerPackage package
    ) =>
        package.Descriptor.Plugin == installation
        && package.Manifest?.Manifest.Id == installation.PluginId
        && package.Manifest.Manifest.Release == installation.Release;

    private static string EncodeTag(string tag)
    {
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(tag));
        return encoded.TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static void DeleteFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static PluginLifecycleSafeDetail SafeRemovalDetail() =>
        PluginLifecycleSafeDetail.TryCreate(
            "Marketplace package data could not be deleted.",
            out var detail
        )
            ? detail
            : throw new InvalidOperationException("Invalid package removal detail.");
}

internal sealed class MarketplacePluginLifecyclePackageResolver(
    PluginMarketplacePackageStore packages
) : IPluginLifecyclePackageResolver
{
    public ValueTask<PluginLifecyclePackageResolution> ResolveAsync(
        PluginInstallationIdentity installation,
        PluginPackageOperationId packageOperationId,
        CancellationToken cancellationToken
    ) => packages.ResolveAsync(installation, packageOperationId, cancellationToken);
}
