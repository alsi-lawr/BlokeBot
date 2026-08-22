using BlokeBot.Persistence;
using BlokeBot.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace BlokeBot.Core.Features.Overlays;

internal sealed partial class OverlayCueService
{
    private async Task<long> WriteUploadAsync(
        Stream content,
        string tempPath,
        CancellationToken cancellationToken
    )
    {
        await using var destination = new FileStream(
            tempPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            81920,
            FileOptions.Asynchronous | FileOptions.WriteThrough
        );
        var length = await CopyBoundedAsync(
            content,
            destination,
            _options.Overlays.Media.MaximumUploadBytes,
            cancellationToken
        );
        await destination.FlushAsync(cancellationToken);
        return length;
    }

    private async Task<bool> WouldExceedQuotaAsync(
        BlokeBotDbContext db,
        int hostId,
        OverlayMediaDocument? replacedDocument,
        long? replacedReferenceId,
        long addedBytes,
        CancellationToken cancellationToken
    )
    {
        var documents = await db
            .OverlayMediaAssets.Where(reference => reference.HostId == hostId)
            .Select(reference => new
            {
                reference.Id,
                reference.DocumentId,
                reference.Document.ByteLength,
            })
            .ToArrayAsync(cancellationToken);
        var total = documents
            .GroupBy(reference => reference.DocumentId)
            .Sum(group => group.First().ByteLength);
        if (
            replacedDocument is not null
            && documents.Count(reference => reference.DocumentId == replacedDocument.Id) == 1
            && documents.Any(reference => reference.Id == replacedReferenceId)
        )
        {
            total -= replacedDocument.ByteLength;
        }
        return total + addedBytes > _options.Overlays.Media.MaximumHostStorageBytes;
    }

    private string DocumentDirectory()
    {
        var directory = OverlayMediaDirectory.DocumentDirectory(_options.DatabasePath);
        _ = Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                directory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            );
        }
        return directory;
    }

    private string DocumentPath(string storageKey) => Path.Combine(DocumentDirectory(), storageKey);

    private static async Task<long> CopyBoundedAsync(
        Stream source,
        Stream destination,
        long maximumBytes,
        CancellationToken cancellationToken
    )
    {
        var buffer = new byte[81920];
        long total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                return total;
            }
            total += read;
            if (total > maximumBytes)
            {
                throw new UploadTooLargeException();
            }
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    private static OverlayMediaAssetView ToView(OverlayMediaAsset value, long chargedByteLength) =>
        new(
            value.PublicId,
            value.Name,
            value.Document.ContentType,
            value.Document.ByteLength,
            value.ContentRevision,
            value.Document.State == OverlayMediaDocumentState.Available,
            chargedByteLength,
            AsOffset(value.UpdatedAtUtc)
        );

    private void TryDelete(string path) => _ = fileDeletion.Delete(path);

    private sealed class UploadTooLargeException : Exception;
}
