using System.Formats.Tar;
using System.IO.Compression;

namespace BlokeBot.Core.Features.Plugins;

internal abstract record PluginMarketplaceArchiveReadOutcome
{
    private PluginMarketplaceArchiveReadOutcome() { }

    internal sealed record Accepted : PluginMarketplaceArchiveReadOutcome;

    internal sealed record Rejected : PluginMarketplaceArchiveReadOutcome;
}

internal sealed class PluginMarketplaceArchiveReader
{
    internal async ValueTask<PluginMarketplaceArchiveReadOutcome> ExtractAsync(
        string compressedArchive,
        string packagePath,
        string destination,
        CancellationToken cancellationToken
    )
    {
        if (!MarketplacePackagePath.IsCanonical(packagePath) || !Directory.Exists(destination))
        {
            return new PluginMarketplaceArchiveReadOutcome.Rejected();
        }

        try
        {
            await using var compressed = new FileStream(
                compressedArchive,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan
            );
            await using var gzip = new GZipStream(
                compressed,
                CompressionMode.Decompress,
                leaveOpen: false
            );
            using var tar = new TarReader(gzip, leaveOpen: false);
            var exactPaths = new HashSet<string>(StringComparer.Ordinal);
            var foldedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string? archiveRoot = null;
            var selectedEntries = 0;
            while (await tar.GetNextEntryAsync(copyData: false, cancellationToken) is { } entry)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!TryArchiveRelativePath(entry.Name, ref archiveRoot, out var relativePath))
                {
                    return new PluginMarketplaceArchiveReadOutcome.Rejected();
                }

                if (relativePath.Length > 0)
                {
                    var collisionPath = relativePath.TrimEnd('/');
                    if (!exactPaths.Add(collisionPath) || !foldedPaths.Add(collisionPath))
                    {
                        return new PluginMarketplaceArchiveReadOutcome.Rejected();
                    }
                }

                if (entry.EntryType is TarEntryType.SymbolicLink or TarEntryType.HardLink)
                {
                    return new PluginMarketplaceArchiveReadOutcome.Rejected();
                }

                if (
                    entry.EntryType
                    is not (
                        TarEntryType.Directory
                        or TarEntryType.RegularFile
                        or TarEntryType.V7RegularFile
                    )
                )
                {
                    return new PluginMarketplaceArchiveReadOutcome.Rejected();
                }

                var selected = TrySelectedPath(relativePath, packagePath, out var selectedPath);
                if (
                    selected
                    && selectedPath.Length > 0
                    && !MarketplacePackagePath.IsCanonical(selectedPath)
                )
                {
                    return new PluginMarketplaceArchiveReadOutcome.Rejected();
                }

                if (entry.EntryType == TarEntryType.Directory)
                {
                    if (selected && selectedPath.Length > 0)
                    {
                        _ = Directory.CreateDirectory(
                            ResolveContainedPath(destination, selectedPath)
                        );
                        selectedEntries++;
                    }

                    continue;
                }

                if (entry.Length < 0 || (selected && selectedPath.Length == 0))
                {
                    return new PluginMarketplaceArchiveReadOutcome.Rejected();
                }

                if (selected)
                {
                    var outputPath = ResolveContainedPath(destination, selectedPath);
                    _ = Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                    await using var output = new FileStream(
                        outputPath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None,
                        bufferSize: 64 * 1024,
                        FileOptions.Asynchronous | FileOptions.SequentialScan
                    );
                    if (!await CopyEntryAsync(entry, output, cancellationToken))
                    {
                        return new PluginMarketplaceArchiveReadOutcome.Rejected();
                    }

                    selectedEntries++;
                }
                else if (!await CopyEntryAsync(entry, Stream.Null, cancellationToken))
                {
                    return new PluginMarketplaceArchiveReadOutcome.Rejected();
                }
            }

            return selectedEntries == 0
                ? new PluginMarketplaceArchiveReadOutcome.Rejected()
                : new PluginMarketplaceArchiveReadOutcome.Accepted();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
            when (exception
                    is InvalidDataException
                        or IOException
                        or UnauthorizedAccessException
                        or NotSupportedException
            )
        {
            return new PluginMarketplaceArchiveReadOutcome.Rejected();
        }
    }

    private static bool TryArchiveRelativePath(
        string path,
        ref string? archiveRoot,
        out string relativePath
    )
    {
        relativePath = string.Empty;
        if (
            string.IsNullOrEmpty(path)
            || path[0] == '/'
            || path.Contains('\\', StringComparison.Ordinal)
            || (path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':')
        )
        {
            return false;
        }

        var trimmed = path.TrimEnd('/');
        var segments = trimmed.Split('/');
        if (segments.Any(segment => segment is "" or "." or "..") || segments[0].Length == 0)
        {
            return false;
        }

        archiveRoot ??= segments[0];
        if (!string.Equals(archiveRoot, segments[0], StringComparison.Ordinal))
        {
            return false;
        }

        relativePath = segments.Length == 1 ? string.Empty : string.Join('/', segments[1..]);
        return true;
    }

    private static bool TrySelectedPath(
        string archiveRelativePath,
        string packagePath,
        out string selectedPath
    )
    {
        selectedPath = string.Empty;
        if (string.Equals(archiveRelativePath, packagePath, StringComparison.Ordinal))
        {
            return true;
        }

        var prefix = $"{packagePath}/";
        if (!archiveRelativePath.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        selectedPath = archiveRelativePath[prefix.Length..];
        return true;
    }

    private static async ValueTask<bool> CopyEntryAsync(
        TarEntry entry,
        Stream destination,
        CancellationToken cancellationToken
    )
    {
        if (entry.DataStream is null)
        {
            return entry.Length == 0;
        }

        await entry.DataStream.CopyToAsync(destination, cancellationToken);
        return true;
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
            : throw new InvalidDataException("Archive entry escaped the staging root.");
    }
}
