using System.Collections.Immutable;
using BlokeBot.Plugins.Contracts;

namespace BlokeBot.Plugins.Runtime;

public sealed record PreparedPluginWorkerPackage(
    PluginWorkerPackageDescriptor Descriptor,
    string PackageRoot
)
{
    public ValidatedPluginManifest? Manifest { get; init; }
}

public enum PluginPackageMaterializationFailureCode
{
    InvalidPackage,
    DestinationExists,
}

public sealed record PluginPackageMaterializationFailure(
    PluginPackageMaterializationFailureCode Code,
    IReadOnlyList<PluginPackageError> PackageErrors
);

public abstract record PluginPackageMaterializationOutcome
{
    private PluginPackageMaterializationOutcome() { }

    public sealed record Prepared(PreparedPluginWorkerPackage Package)
        : PluginPackageMaterializationOutcome;

    public sealed record Rejected(PluginPackageMaterializationFailure Failure)
        : PluginPackageMaterializationOutcome;
}

public static class PluginWorkerPackageMaterializer
{
    public static async ValueTask<PluginPackageMaterializationOutcome> MaterializeAsync(
        IReadOnlyList<PluginPackageEntry> entries,
        PluginHostCompatibilityTarget target,
        string destination,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        var validation = PluginPackageValidator.Validate(entries, target);
        if (validation is PluginPackageValidationOutcome.Rejected rejected)
        {
            return new PluginPackageMaterializationOutcome.Rejected(
                new(PluginPackageMaterializationFailureCode.InvalidPackage, rejected.Errors)
            );
        }

        var fullDestination = Path.GetFullPath(destination);
        if (Directory.Exists(fullDestination) || File.Exists(fullDestination))
        {
            return new PluginPackageMaterializationOutcome.Rejected(
                new(
                    PluginPackageMaterializationFailureCode.DestinationExists,
                    Array.Empty<PluginPackageError>()
                )
            );
        }

        var temporary = $"{fullDestination}.preparing-{Guid.NewGuid():N}";
        try
        {
            _ = Directory.CreateDirectory(temporary);
            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var outputPath = ResolveContainedPath(temporary, entry.Path);
                switch (entry)
                {
                    case PluginPackageEntry.Directory:
                        _ = Directory.CreateDirectory(outputPath);
                        break;
                    case PluginPackageEntry.File file:
                        _ = Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                        await File.WriteAllBytesAsync(
                            outputPath,
                            file.Content.ToArray(),
                            cancellationToken
                        );
                        break;
                    case PluginPackageEntry.SymbolicLink or PluginPackageEntry.HardLink:
                        throw new InvalidOperationException(
                            "Validated packages cannot contain links."
                        );
                }
            }

            Directory.Move(temporary, fullDestination);
        }
        catch
        {
            if (Directory.Exists(temporary))
            {
                Directory.Delete(temporary, recursive: true);
            }

            throw;
        }

        var manifest = ((PluginPackageValidationOutcome.Accepted)validation).Manifest.Manifest;
        var descriptor = new PluginWorkerPackageDescriptor(
            new(manifest.Id, manifest.Release),
            target.RuntimeIdentifier,
            manifest.EntryModule,
            manifest
                .LuaModules.Select(module => new PluginWorkerLuaModule(module.Id, module.Path))
                .ToImmutableArray()
        );
        return new PluginPackageMaterializationOutcome.Prepared(
            new(descriptor, fullDestination)
            {
                Manifest = ((PluginPackageValidationOutcome.Accepted)validation).Manifest,
            }
        );
    }

    private static string ResolveContainedPath(string root, string canonicalPath)
    {
        var output = Path.GetFullPath(
            Path.Combine(root, canonicalPath.Replace('/', Path.DirectorySeparatorChar))
        );
        var prefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : $"{root}{Path.DirectorySeparatorChar}";
        return output.StartsWith(prefix, StringComparison.Ordinal)
            ? output
            : throw new InvalidOperationException("Validated package path escaped its root.");
    }
}
