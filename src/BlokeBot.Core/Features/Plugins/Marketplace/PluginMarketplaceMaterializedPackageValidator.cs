using System.Collections.Immutable;
using BlokeBot.Plugins.Contracts;
using BlokeBot.Plugins.Runtime;

namespace BlokeBot.Core.Features.Plugins;

internal abstract record PluginMarketplaceMaterializedPackageValidationOutcome
{
    private PluginMarketplaceMaterializedPackageValidationOutcome() { }

    internal sealed record Accepted(PreparedPluginWorkerPackage Package)
        : PluginMarketplaceMaterializedPackageValidationOutcome;

    internal sealed record Rejected : PluginMarketplaceMaterializedPackageValidationOutcome;
}

internal static class PluginMarketplaceMaterializedPackageValidator
{
    internal static async ValueTask<PluginMarketplaceMaterializedPackageValidationOutcome> ValidateAsync(
        string packageRoot,
        PluginHostCompatibilityTarget target,
        CancellationToken cancellationToken
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageRoot);
        ArgumentNullException.ThrowIfNull(target);
        var root = Path.GetFullPath(packageRoot);
        if (!Directory.Exists(root))
        {
            return new PluginMarketplaceMaterializedPackageValidationOutcome.Rejected();
        }

        var indexed = Index(root, cancellationToken);
        if (indexed is null || !indexed.Files.Contains(PluginPackage.ManifestPath))
        {
            return new PluginMarketplaceMaterializedPackageValidationOutcome.Rejected();
        }

        PluginManifestValidationOutcome manifestOutcome;
        try
        {
            await using var manifest = new FileStream(
                ResolveContainedPath(root, PluginPackage.ManifestPath),
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan
            );
            manifestOutcome = await PluginManifestJson.ValidateUnboundedAsync(
                manifest,
                target,
                cancellationToken
            );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new PluginMarketplaceMaterializedPackageValidationOutcome.Rejected();
        }

        if (manifestOutcome is not PluginManifestValidationOutcome.Accepted accepted)
        {
            return new PluginMarketplaceMaterializedPackageValidationOutcome.Rejected();
        }

        var manifestModel = accepted.Manifest.Manifest;
        var declarations = manifestModel
            .LuaModules.Select(module => module.Path)
            .Concat(manifestModel.Assets.Select(asset => asset.Path))
            .Concat(manifestModel.Payloads.Select(payload => payload.Path))
            .Prepend(PluginPackage.ManifestPath)
            .ToHashSet(StringComparer.Ordinal);
        if (
            !indexed.Files.SetEquals(declarations)
            || indexed.Directories.Any(directory =>
                !declarations.Any(path =>
                    path.StartsWith($"{directory}/", StringComparison.Ordinal)
                )
            )
        )
        {
            return new PluginMarketplaceMaterializedPackageValidationOutcome.Rejected();
        }

        var descriptor = new PluginWorkerPackageDescriptor(
            new(manifestModel.Id, manifestModel.Release),
            target.RuntimeIdentifier,
            manifestModel.EntryModule,
            manifestModel
                .LuaModules.Select(module => new PluginWorkerLuaModule(module.Id, module.Path))
                .ToImmutableArray()
        );
        return new PluginMarketplaceMaterializedPackageValidationOutcome.Accepted(
            new(descriptor, root) { Manifest = accepted.Manifest }
        );
    }

    private static PackageIndex? Index(string root, CancellationToken cancellationToken)
    {
        var exactPaths = new HashSet<string>(StringComparer.Ordinal);
        var foldedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new HashSet<string>(StringComparer.Ordinal);
        var directories = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>();
        pending.Push(root);
        try
        {
            while (pending.TryPop(out var directory))
            {
                foreach (var path in Directory.EnumerateFileSystemEntries(directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var attributes = File.GetAttributes(path);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        return null;
                    }

                    var relative = Path.GetRelativePath(root, path)
                        .Replace(Path.DirectorySeparatorChar, '/');
                    if (
                        !MarketplacePackagePath.IsCanonical(relative)
                        || !exactPaths.Add(relative)
                        || !foldedPaths.Add(relative)
                    )
                    {
                        return null;
                    }

                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        _ = directories.Add(relative);
                        pending.Push(path);
                    }
                    else
                    {
                        _ = files.Add(relative);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return new(files, directories);
    }

    private static string ResolveContainedPath(string root, string canonicalPath)
    {
        var path = Path.GetFullPath(
            Path.Combine(root, canonicalPath.Replace('/', Path.DirectorySeparatorChar))
        );
        var prefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : $"{root}{Path.DirectorySeparatorChar}";
        return path.StartsWith(prefix, StringComparison.Ordinal)
            ? path
            : throw new InvalidOperationException("Package path escaped its staging root.");
    }

    private sealed record PackageIndex(HashSet<string> Files, HashSet<string> Directories);
}
